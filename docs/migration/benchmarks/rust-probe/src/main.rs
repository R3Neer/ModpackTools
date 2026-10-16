// Diagnostic workload only; not a ModpackTools core or inventory implementation.
use sha2::{Digest, Sha256};
use std::{error::Error, fs, io::Read, path::{Path, PathBuf}};

fn walk(root: &Path, files: &mut Vec<PathBuf>) -> Result<(), Box<dyn Error>> {
    for entry in fs::read_dir(root)? {
        let entry = entry?;
        if entry.file_name() == ".git" { continue; }
        let kind = entry.file_type()?;
        if kind.is_symlink() { return Err("Diagnostic workload must not contain links".into()); }
        if kind.is_dir() { walk(&entry.path(), files)?; } else if kind.is_file() { files.push(entry.path()); }
    }
    Ok(())
}
fn relative(root: &Path, file: &Path) -> Result<String, Box<dyn Error>> {
    Ok(file.strip_prefix(root)?.to_str().ok_or("Non-Unicode path")?.replace('\\', "/"))
}
fn hex(bytes: &[u8]) -> String { bytes.iter().map(|b| format!("{b:02x}")).collect() }
fn main() -> Result<(), Box<dyn Error>> {
    let args: Vec<String> = std::env::args().collect();
    if args.len() != 3 || args[1] != "--hash" {
        println!("{{\"ok\":true,\"benchmark\":\"startup-only\"}}"); return Ok(());
    }
    let root = Path::new(&args[2]);
    let mut paths = Vec::new(); walk(root, &mut paths)?;
    let mut files: Vec<(String, PathBuf)> = paths.into_iter().map(|p| relative(root, &p).map(|r| (r,p))).collect::<Result<_,_>>()?;
    files.sort_by(|a,b| a.0.cmp(&b.0));
    let mut fingerprint = Sha256::new(); let mut bytes = 0u64; let mut buffer = [0u8; 65536];
    for (name, path) in &files {
        let mut file = fs::File::open(path)?; bytes += file.metadata()?.len();
        let mut hash = Sha256::new();
        loop { let n = file.read(&mut buffer)?; if n == 0 { break; } hash.update(&buffer[..n]); }
        fingerprint.update(format!("{}:{}\n", name, hex(&hash.finalize())).as_bytes());
    }
    println!("{{\"ok\":true,\"benchmark\":\"tree-hash-only\",\"files\":{},\"bytes\":{},\"fingerprint\":\"{}\"}}", files.len(), bytes, hex(&fingerprint.finalize()));
    Ok(())
}
