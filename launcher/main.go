// NeonDeck portable launcher for Windows 11.
// Cross-build: GOOS=windows GOARCH=amd64 CGO_ENABLED=0 go build -ldflags "-H windowsgui -s -w" -o NeonDeck.exe .
// This launcher starts the cached WinForms dashboard. If the dashboard has not yet
// been compiled, it invokes the .NET Framework compiler silently (no CMD console).
package main

import (
 "bytes"
 "fmt"
 "os"
 "os/exec"
 "path/filepath"
 "strings"
 "syscall"
 "unsafe"
)

const version = "1.5.0"

var (
 user32 = syscall.NewLazyDLL("user32.dll")
 procMessageBoxW = user32.NewProc("MessageBoxW")
)

func message(title, body string) {
 t, _ := syscall.UTF16PtrFromString(title)
 b, _ := syscall.UTF16PtrFromString(body)
 procMessageBoxW.Call(0, uintptr(unsafe.Pointer(b)), uintptr(unsafe.Pointer(t)), uintptr(0x10))
}

func findCompiler() string {
 root := os.Getenv("WINDIR")
 if root == "" { root = `C:\Windows` }
 paths := []string{
  filepath.Join(root,"Microsoft.NET","Framework64","v4.0.30319","csc.exe"),
  filepath.Join(root,"Microsoft.NET","Framework","v4.0.30319","csc.exe"),
 }
 for _, p := range paths { if fi,err:=os.Stat(p);err==nil && !fi.IsDir(){return p} }
 return ""
}

func exists(path string) bool { fi,e:=os.Stat(path);return e==nil && !fi.IsDir() }
func main() {
 self, e := os.Executable()
 if e != nil { message("NeonDeck", "Nie mogę odczytać lokalizacji programu.");return }
 base := filepath.Dir(self)
 app := filepath.Join(base,"NeonDeckApp.exe")
 marker := filepath.Join(base,"NeonDeck.build")
 src := filepath.Join(base,"NeonDeck.cs")
 icon := filepath.Join(base,"NeonDeck.ico")
 verBytes,_:=os.ReadFile(marker)
 fresh := exists(app) && strings.TrimSpace(string(verBytes))==version
 if !fresh {
  compiler:=findCompiler()
  if compiler=="" {
   message("NeonDeck — brak kompilatora", "Nie znaleziono kompilatora .NET Framework 4.x.\nProgram nie wymaga instalatora, ale przy pierwszym uruchomieniu musi zbudować moduł aplikacji.\nSprawdź README_PL.md.")
   return
  }
  if !exists(src) || !exists(icon) {
   message("NeonDeck — brakuje plików", "Brakuje NeonDeck.cs lub NeonDeck.ico.\nRozpakuj całe archiwum ZIP do jednego folderu.")
   return
  }
  // Build to a temporary target and only replace the cached program on success.
  tmp := filepath.Join(base,"NeonDeckApp.building.exe")
  _ = os.Remove(tmp)
  args := []string{"/nologo", "/target:winexe", "/optimize+", "/codepage:65001", "/win32icon:"+icon, "/out:"+tmp,
   "/r:System.dll", "/r:System.Core.dll", "/r:System.Drawing.dll", "/r:System.Windows.Forms.dll", "/r:System.Management.dll", src}
  cmd:=exec.Command(compiler,args...)
  cmd.Dir=base
  cmd.SysProcAttr=&syscall.SysProcAttr{HideWindow:true,CreationFlags:0x08000000}
  var out bytes.Buffer
  cmd.Stdout=&out;cmd.Stderr=&out
  if err:=cmd.Run();err!=nil {
   log:=fmt.Sprintf("NeonDeck v%s build failed: %v\r\nCompiler: %s\r\n\r\n%s",version,err,compiler,out.String())
   _=os.WriteFile(filepath.Join(base,"NeonDeck_build_error.txt"),[]byte(log),0644)
   if len(log)>1900{log=log[:1900]+"..."}
   message("NeonDeck — błąd budowania",log+"\n\nSzczegóły: NeonDeck_build_error.txt")
   _=os.Remove(tmp)
   return
  }
  if exists(app) { if e:=os.Remove(app);e!=nil {
    message("NeonDeck — zamknij poprzednią wersję", "Zamknij działający NeonDeck przez Exit NeonDeck i uruchom program ponownie.")
    _=os.Remove(tmp)
    return
  }}
  if e:=os.Rename(tmp,app);e!=nil {
    message("NeonDeck", "Nie mogę zapisać NeonDeckApp.exe.\nRozpakuj program do folderu, do którego masz prawo zapisu.")
    _=os.Remove(tmp)
    return
  }
  _=os.WriteFile(marker,[]byte(version+"\r\n"),0644)
 }
 cmd:=exec.Command(app)
 cmd.Dir=base
 // WinForms needs ordinary GUI startup so the gadget windows are visible.
 // It is already built with /target:winexe, so no console is created.
 if e:=cmd.Start();e!=nil {message("NeonDeck — błąd uruchamiania", "Nie można uruchomić modułu programu:\n"+e.Error()); return}
 _=cmd.Process.Release()
}
