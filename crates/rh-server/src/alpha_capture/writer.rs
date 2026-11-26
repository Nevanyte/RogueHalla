//! The only layer that touches the filesystem or runs age. Each stream has its own bounded queue and dedicated worker.

use serde::Serialize;
use std::fs::{self, DirBuilder, File, Metadata, OpenOptions};
use std::io::{self, BufWriter, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering::Relaxed};
use std::sync::{mpsc, Arc};
use std::time::{Duration, Instant};

const ENCRYPTION_TIMEOUT: Duration = Duration::from_secs(10);
const DISK_OVERHEAD: u64 = 64 * 1024;
const MAX_DIRECTORY_ENTRIES: usize = 8192;

pub(super) trait FileJob: Serialize + Send + 'static {
    const KIND: &'static str;
}

pub(super) struct Limits {
    pub(super) queue: usize,
    pub(super) plaintext_bytes: u64,
    pub(super) disk_files: usize,
    pub(super) disk_bytes: u64,
}

#[derive(Default)]
pub(super) struct StreamStats {
    configured: AtomicBool,
    started: AtomicBool,
    start_failed: AtomicBool,
    pending: AtomicU64,
    saved: AtomicU64,
    dropped_limit: AtomicU64,
    dropped_queue: AtomicU64,
    writer_unavailable: AtomicU64,
    failed: AtomicU64,
}

impl StreamStats {
    pub(super) fn configure(&self, configured: bool) {
        self.configured.store(configured, Relaxed);
    }

    pub(super) fn fail_start(&self, kind: &str, error: &str) {
        self.start_failed.store(true, Relaxed);
        eprintln!("[alpha-capture] stream={kind} event=start_failed reason={error}");
    }

    pub(super) fn pending(&self) -> u64 {
        self.pending.load(Relaxed)
    }

    pub(super) fn health(&self) -> serde_json::Value {
        serde_json::json!({
            "configured": self.configured.load(Relaxed),
            "started": self.started.load(Relaxed),
            "startFailed": self.start_failed.load(Relaxed),
            "pending": self.pending(),
            "saved": self.saved.load(Relaxed),
            "droppedLimit": self.dropped_limit.load(Relaxed),
            "droppedQueue": self.dropped_queue.load(Relaxed),
            "writerUnavailable": self.writer_unavailable.load(Relaxed),
            "failed": self.failed.load(Relaxed),
        })
    }
}

pub(super) struct Stream<T> {
    queue: mpsc::SyncSender<(String, T)>,
    stats: Arc<StreamStats>,
}

impl<T> Clone for Stream<T> {
    fn clone(&self) -> Self {
        Self {
            queue: self.queue.clone(),
            stats: self.stats.clone(),
        }
    }
}

impl<T: FileJob> Stream<T> {
    pub(super) fn start(
        dir: PathBuf,
        recipient: String,
        stats: Arc<StreamStats>,
        limits: Limits,
    ) -> Result<Self, &'static str> {
        let spool = Spool::open(dir, recipient, T::KIND, limits)?;
        let (queue, rx) = mpsc::sync_channel::<(String, T)>(spool.limits.queue);
        let worker_stats = stats.clone();
        std::thread::Builder::new()
            .name(format!("alpha-{}-writer", T::KIND))
            .spawn(move || {
                let mut previous = (0, 0);
                loop {
                    let current = (
                        worker_stats.dropped_queue.load(Relaxed),
                        worker_stats.dropped_limit.load(Relaxed),
                    );
                    if current != previous {
                        eprintln!(
                            "[alpha-capture] stream={} event=dropped_artifacts queue={} limit={}",
                            T::KIND,
                            current.0,
                            current.1
                        );
                        previous = current;
                    }
                    let (name, job) = match rx.recv_timeout(Duration::from_secs(1)) {
                        Ok(job) => job,
                        Err(mpsc::RecvTimeoutError::Timeout) => continue,
                        Err(mpsc::RecvTimeoutError::Disconnected) => break,
                    };
                    match spool.write(&name, &job) {
                        Ok(()) => {
                            worker_stats.saved.fetch_add(1, Relaxed);
                        }
                        Err(WriteFailure::Limit(reason)) => {
                            log_drop(&worker_stats.dropped_limit, T::KIND, reason);
                        }
                        Err(WriteFailure::Failure(reason)) => {
                            log_drop(&worker_stats.failed, T::KIND, reason);
                        }
                    }
                    worker_stats.pending.fetch_sub(1, Relaxed);
                }
                worker_stats.started.store(false, Relaxed);
            })
            .map_err(|_| "cannot start spool writer")?;
        stats.started.store(true, Relaxed);
        Ok(Self { queue, stats })
    }

    pub(super) fn enqueue(&self, name: String, job: T) {
        self.stats.pending.fetch_add(1, Relaxed);
        if let Err(error) = self.queue.try_send((name, job)) {
            self.stats.pending.fetch_sub(1, Relaxed);
            let counter = match error {
                mpsc::TrySendError::Full(_) => &self.stats.dropped_queue,
                mpsc::TrySendError::Disconnected(_) => &self.stats.writer_unavailable,
            };
            counter.fetch_add(1, Relaxed);
        }
    }

    pub(super) fn drop_limit(&self) {
        self.stats.dropped_limit.fetch_add(1, Relaxed);
    }
}

fn log_drop(counter: &AtomicU64, kind: &str, reason: &str) {
    let previous = counter.fetch_add(1, Relaxed);
    if previous == 0 || previous.saturating_add(1).is_power_of_two() {
        eprintln!(
            "[alpha-capture] stream={kind} event=artifact_dropped reason={reason} count={}",
            previous.saturating_add(1)
        );
    }
}

#[cfg(unix)]
pub(super) fn private_mode(metadata: &Metadata) -> bool {
    use std::os::unix::fs::PermissionsExt;
    metadata.permissions().mode() & 0o077 == 0
}

#[cfg(not(unix))]
pub(super) fn private_mode(_metadata: &Metadata) -> bool {
    false
}

pub(super) fn validate_recipient(recipient: &str) -> Result<(), &'static str> {
    let Some(payload) = recipient.strip_prefix("age1") else {
        return Err("expected an age X25519 recipient");
    };
    if payload.len() != 58
        || !payload
            .bytes()
            .all(|byte| b"qpzry9x8gf2tvdw0s3jn54khce6mua7l".contains(&byte))
    {
        return Err("expected an age X25519 recipient");
    }
    let child = age(recipient)
        .stdin(Stdio::null())
        .stdout(Stdio::null())
        .spawn()
        .map_err(|_| "could not start age")?;
    if !wait_for_age(child, ENCRYPTION_TIMEOUT) {
        return Err("age failed to validate the recipient");
    }
    Ok(())
}

fn age(recipient: &str) -> Command {
    let mut command = Command::new("age");
    command
        .env_clear()
        .env("PATH", std::env::var_os("PATH").unwrap_or_default());
    command
        .args(["--encrypt", "--recipient", recipient])
        .stderr(Stdio::null());
    command
}

fn wait_for_age(mut child: Child, deadline: Duration) -> bool {
    let started = Instant::now();
    loop {
        match child.try_wait() {
            Ok(Some(status)) => return status.success(),
            Ok(None) if started.elapsed() < deadline => {
                std::thread::sleep(Duration::from_millis(10))
            }
            _ => {
                let _ = child.kill();
                let _ = child.wait();
                return false;
            }
        }
    }
}

#[derive(Debug, PartialEq, Eq)]
enum WriteFailure {
    Limit(&'static str),
    Failure(&'static str),
}

struct Spool {
    dir: PathBuf,
    recipient: String,
    prefix: String,
    limits: Limits,
}

impl Spool {
    fn open(
        dir: PathBuf,
        recipient: String,
        kind: &str,
        limits: Limits,
    ) -> Result<Self, &'static str> {
        let mut builder = DirBuilder::new();
        #[cfg(unix)]
        {
            use std::os::unix::fs::DirBuilderExt;
            builder.mode(0o700);
        }
        match builder.create(&dir) {
            Ok(()) => {
                let parent = dir
                    .parent()
                    .filter(|p| !p.as_os_str().is_empty())
                    .unwrap_or(Path::new("."));
                File::open(parent)
                    .and_then(|parent| parent.sync_all())
                    .map_err(|_| "cannot sync spool parent")?;
            }
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => {}
            Err(_) => return Err("cannot create spool directory"),
        }
        let metadata = fs::symlink_metadata(&dir).map_err(|_| "cannot inspect spool directory")?;
        if !metadata.is_dir() || !private_mode(&metadata) {
            return Err("spool must be a private directory");
        }
        let spool = Self {
            dir,
            recipient,
            prefix: format!("{kind}-"),
            limits,
        };
        // One server writer per stream directory. Finalized files are the restart-surviving handoff.
        spool
            .inventory(true)
            .map_err(|_| "cannot inspect or recover spool directory")?;
        Ok(spool)
    }

    fn inventory(&self, clean_partials: bool) -> Result<(usize, u64), WriteFailure> {
        let failure = WriteFailure::Failure("spool_read_failed");
        let mut count = 0usize;
        let mut bytes = 0u64;
        for (i, entry) in fs::read_dir(&self.dir)
            .map_err(|_| WriteFailure::Failure("spool_read_failed"))?
            .enumerate()
        {
            if i == MAX_DIRECTORY_ENTRIES {
                return Err(WriteFailure::Limit("directory_entry_limit"));
            }
            let entry = entry.map_err(|_| WriteFailure::Failure("spool_read_failed"))?;
            let name = entry.file_name();
            let Some(name) = name.to_str() else {
                continue;
            };
            if name.starts_with(&self.prefix) && name.ends_with(".json.age") {
                match entry.metadata() {
                    Ok(metadata) if metadata.is_file() => {
                        count += 1;
                        bytes = bytes.saturating_add(metadata.len());
                    }
                    Ok(_) => return Err(failure),
                    // The uploader may remove a committed artifact while we scan.
                    Err(error) if error.kind() == io::ErrorKind::NotFound => {}
                    Err(_) => return Err(failure),
                }
            } else if clean_partials
                && name.strip_prefix('.').is_some_and(|name| {
                    name.starts_with(&self.prefix) && name.ends_with(".json.age.partial")
                })
            {
                fs::remove_file(entry.path())
                    .map_err(|_| WriteFailure::Failure("spool_cleanup_failed"))?;
            }
        }
        Ok((count, bytes))
    }

    fn write(&self, name: &str, job: &impl Serialize) -> Result<(), WriteFailure> {
        let (count, bytes) = self.inventory(false)?;
        let reserved = self.limits.plaintext_bytes + DISK_OVERHEAD;
        if count >= self.limits.disk_files
            || bytes.saturating_add(reserved) > self.limits.disk_bytes
        {
            return Err(WriteFailure::Limit("spool_limit"));
        }
        let final_path = self.dir.join(name);
        if final_path.exists() {
            return Err(WriteFailure::Failure("artifact_exists"));
        }
        let partial_path = self.dir.join(format!(".{name}.partial"));
        let mut options = OpenOptions::new();
        options.write(true).create_new(true);
        #[cfg(unix)]
        {
            use std::os::unix::fs::OpenOptionsExt;
            options.mode(0o600);
        }
        let output = options
            .open(&partial_path)
            .map_err(|_| WriteFailure::Failure("spool_create_failed"))?;
        let result = self.encrypt(job, &output).and_then(|()| {
            let len = output
                .metadata()
                .map_err(|_| WriteFailure::Failure("spool_stat_failed"))?
                .len();
            if len > reserved {
                return Err(WriteFailure::Limit("ciphertext_byte_limit"));
            }
            output
                .sync_all()
                .map_err(|_| WriteFailure::Failure("file_sync_failed"))?;
            fs::rename(&partial_path, final_path)
                .map_err(|_| WriteFailure::Failure("spool_rename_failed"))?;
            File::open(&self.dir)
                .and_then(|dir| dir.sync_all())
                .map_err(|_| WriteFailure::Failure("directory_sync_failed"))
        });
        if result.is_err() {
            let _ = fs::remove_file(&partial_path);
        }
        result
    }

    fn encrypt(&self, job: &impl Serialize, output: &File) -> Result<(), WriteFailure> {
        let output = output
            .try_clone()
            .map_err(|_| WriteFailure::Failure("spool_clone_failed"))?;
        let mut child = age(&self.recipient)
            .stdin(Stdio::piped())
            .stdout(output)
            .spawn()
            .map_err(|_| WriteFailure::Failure("age_spawn_failed"))?;
        let stdin = child.stdin.take().expect("age stdin is piped");
        // The watchdog owns the child and can close the pipe even while serialization is waiting in a write.
        std::thread::scope(|scope| {
            let watchdog = scope.spawn(move || wait_for_age(child, ENCRYPTION_TIMEOUT));
            let mut writer = BufWriter::new(LimitedWriter {
                sink: stdin,
                remaining: self.limits.plaintext_bytes,
                hit_limit: false,
            });
            let result = serde_json::to_writer(&mut writer, job)
                .map_err(io::Error::from)
                .and_then(|()| writer.flush());
            let hit_limit = writer.get_ref().hit_limit;
            drop(writer);
            let finished = watchdog.join().unwrap_or(false);
            if hit_limit {
                return Err(WriteFailure::Limit("plaintext_byte_limit"));
            }
            if !finished || result.is_err() {
                return Err(WriteFailure::Failure("encryption_failed"));
            }
            Ok(())
        })
    }
}

struct LimitedWriter<W> {
    sink: W,
    remaining: u64,
    hit_limit: bool,
}

impl<W: Write> Write for LimitedWriter<W> {
    fn write(&mut self, bytes: &[u8]) -> io::Result<usize> {
        if bytes.len() as u64 > self.remaining {
            self.hit_limit = true;
            return Err(io::Error::other("capture byte budget exceeded"));
        }
        let written = self.sink.write(bytes)?;
        self.remaining -= written as u64;
        Ok(written)
    }

    fn flush(&mut self) -> io::Result<()> {
        self.sink.flush()
    }
}

#[cfg(test)]
impl<T: FileJob> Stream<T> {
    pub(super) fn probe(
        stats: Arc<StreamStats>,
        capacity: usize,
    ) -> (Self, mpsc::Receiver<(String, T)>) {
        let (queue, rx) = mpsc::sync_channel(capacity);
        (Self { queue, stats }, rx)
    }
}

#[cfg(test)]
mod tests;
