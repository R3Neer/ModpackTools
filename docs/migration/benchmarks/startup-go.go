// Evaluation fixture only: no ModpackTools functionality.
package main

import (
    "fmt"
    "crypto/sha256"
    "encoding/hex"
    "io"
    "os"
    "path/filepath"
    "sort"
    "strings"
)

func main() {
    if len(os.Args) == 3 && os.Args[1] == "--hash" {
        root := os.Args[2]
        files := []string{}
        err := filepath.WalkDir(root, func(path string, entry os.DirEntry, err error) error {
            if err != nil { return err }
            if entry.Name() == ".git" { if entry.IsDir() { return filepath.SkipDir }; return nil }
            if entry.Type() & os.ModeSymlink != 0 { return fmt.Errorf("diagnostic workload contains link") }
            if !entry.IsDir() { files = append(files, path) }; return nil
        })
        if err != nil { panic(err) }
        relative := func(path string) string { value, err := filepath.Rel(root,path); if err != nil { panic(err) }; return strings.ReplaceAll(value,`\`,`/`) }
        sort.Slice(files,func(i,j int) bool{return relative(files[i]) < relative(files[j])})
        fingerprint := sha256.New(); var bytes int64; buffer:=make([]byte,65536)
        for _,path := range files {
            file,err:=os.Open(path);if err != nil {panic(err)}
            info,err:=file.Stat();if err != nil {file.Close();panic(err)};bytes+=info.Size()
            hash:=sha256.New();_,err=io.CopyBuffer(hash,file,buffer);file.Close();if err != nil {panic(err)}
            fmt.Fprintf(fingerprint,"%s:%s\n",relative(path),hex.EncodeToString(hash.Sum(nil)))
        }
        fmt.Printf("{\"ok\":true,\"benchmark\":\"tree-hash-only\",\"files\":%d,\"bytes\":%d,\"fingerprint\":\"%s\"}\n",len(files),bytes,hex.EncodeToString(fingerprint.Sum(nil)))
        return
    }
    fmt.Println(`{"ok":true,"benchmark":"startup-only"}`)
}
