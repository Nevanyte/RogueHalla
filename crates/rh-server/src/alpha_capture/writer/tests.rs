use super::*;

#[derive(Serialize)]
struct TestJob(&'static str);

impl FileJob for TestJob {
    const KIND: &'static str = "replay";
}

#[test]
fn queue_full_and_worker_loss_are_counted_without_blocking() {
    let stats = Arc::new(StreamStats::default());
    let (stream, rx) = Stream::probe(stats.clone(), 1);
    stream.enqueue("first".into(), TestJob("1"));
    stream.enqueue("second".into(), TestJob("2"));
    assert_eq!(stats.health()["droppedQueue"], 1);
    assert_eq!(stats.pending(), 1);
    assert_eq!(rx.try_recv().unwrap().0, "first");
    drop(rx);
    stream.enqueue("third".into(), TestJob("3"));
    assert_eq!(stats.health()["writerUnavailable"], 1);
    assert_eq!(stats.pending(), 1);
}

#[test]
fn the_plaintext_writer_refuses_bytes_over_the_exact_budget() {
    let mut writer = LimitedWriter {
        sink: Vec::new(),
        remaining: 3,
        hit_limit: false,
    };
    writer.write_all(b"abc").unwrap();
    assert!(writer.write_all(b"d").is_err());
    assert_eq!(writer.sink, b"abc");
    assert!(writer.hit_limit);
}

#[cfg(unix)]
#[test]
#[ignore = "requires age and age-keygen on PATH"]
fn real_age_spool_roundtrip_restart_and_failure() {
    use std::os::unix::fs::PermissionsExt;
    const NAME: &str = "replay-00000000000000000000000000000001.json.age";
    let root = std::env::temp_dir().join(format!(
        "rh-alpha-test-{}",
        crate::control::room_id_hex(crate::room::random16())
    ));
    struct Cleanup(PathBuf);
    impl Drop for Cleanup {
        fn drop(&mut self) {
            let _ = fs::remove_dir_all(&self.0);
        }
    }
    fs::create_dir(&root).unwrap();
    let _cleanup = Cleanup(root.clone());
    let key = root.join("private.agekey");
    let status = Command::new("age-keygen")
        .arg("-o")
        .arg(&key)
        .stderr(Stdio::null())
        .status()
        .unwrap();
    assert!(status.success());
    let public = Command::new("age-keygen")
        .arg("-y")
        .arg(&key)
        .output()
        .unwrap();
    assert!(public.status.success());
    let public = String::from_utf8(public.stdout).unwrap().trim().to_owned();
    validate_recipient(&public).unwrap();
    let spool_dir = root.join("spool");
    let limits = || Limits {
        queue: 1,
        plaintext_bytes: 32,
        disk_files: 1,
        disk_bytes: 1024 * 1024,
    };
    let spool = Spool::open(spool_dir.clone(), public.clone(), "replay", limits()).unwrap();
    assert_eq!(
        spool.write(
            NAME,
            &TestJob("over the 32 byte plaintext limit by some margin")
        ),
        Err(WriteFailure::Limit("plaintext_byte_limit"))
    );
    assert_eq!(fs::read_dir(&spool_dir).unwrap().count(), 0);
    let failed = Spool::open(spool_dir.clone(), "invalid".into(), "replay", limits()).unwrap();
    assert!(matches!(
        failed.write(NAME, &TestJob("never on disk")),
        Err(WriteFailure::Failure("encryption_failed"))
    ));
    assert_eq!(fs::read_dir(&spool_dir).unwrap().count(), 0);
    spool
        .write(NAME, &TestJob("private alpha fixture"))
        .unwrap();
    let final_path = spool_dir.join(NAME);
    assert_eq!(
        fs::metadata(&final_path).unwrap().permissions().mode() & 0o777,
        0o600
    );
    let blob = fs::read(&final_path).unwrap();
    assert!(blob.starts_with(b"age-encryption.org/v1\n"));
    assert!(!blob
        .windows(b"private alpha fixture".len())
        .any(|s| s == b"private alpha fixture"));
    drop(spool);
    fs::write(
        spool_dir.join(format!(".{NAME}.partial")),
        b"unfinished encrypted bytes",
    )
    .unwrap();
    let reopened = Spool::open(spool_dir.clone(), public, "replay", limits()).unwrap();
    assert_eq!(fs::read_dir(spool_dir).unwrap().count(), 1);
    assert_eq!(
        reopened.write(
            "replay-00000000000000000000000000000002.json.age",
            &TestJob("full")
        ),
        Err(WriteFailure::Limit("spool_limit"))
    );
    let plain = Command::new("age")
        .args(["--decrypt", "--identity"])
        .arg(key)
        .arg(final_path)
        .output()
        .unwrap();
    assert!(plain.status.success());
    assert_eq!(
        serde_json::from_slice::<String>(&plain.stdout).unwrap(),
        "private alpha fixture"
    );
}
