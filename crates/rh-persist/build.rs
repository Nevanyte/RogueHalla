// sqlx's `migrate!` embeds the files it finds in `migrations/` but, on stable Rust, does not watch the folder itself:
// without this line a cached build never sees a newly added migration and the binary keeps the old list.
fn main() {
    println!("cargo:rerun-if-changed=migrations");
}
