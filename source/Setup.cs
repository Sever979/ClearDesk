using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;
class Setup {
 static string Target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","ClearDesk");
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();try{
  if(args.Contains("--install")){Install();return;}
  var f=new Form{Text="Установка ClearDesk "+ProductInfo.Version,Size=new Size(650,430),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,Font=new Font("Segoe UI",10)};
  f.Controls.Add(new Label{Text="ClearDesk "+ProductInfo.Version+" — Free\nРазработчик: "+ProductInfo.Developer+"\n"+ProductInfo.Copyright+"\n"+ProductInfo.Email+"\n\nУсловно-бесплатная программа. Базовые функции бесплатны.\nPro с ежемесячной подпиской планируется; продаж пока нет.\n\nУстановка для текущего пользователя:\n"+Target+"\nБудут созданы ярлыки на рабочем столе и в меню «Пуск».",Location=new Point(20,20),Size=new Size(600,240)});
  var license=new Button{Text="Условия использования",Location=new Point(20,330),Size=new Size(230,35)};license.Click+=delegate{ProductInfo.ShowLicense();};f.Controls.Add(license);
  var accept=new CheckBox{Text="Я принимаю условия использования ClearDesk",Location=new Point(20,280),Size=new Size(590,30)};f.Controls.Add(accept);
  var b=new Button{Text="Установить",Enabled=false,Location=new Point(440,330),Size=new Size(150,35)};accept.CheckedChanged+=delegate{b.Enabled=accept.Checked;};b.Click+=delegate{try{Install();MessageBox.Show("ClearDesk установлен.");Process.Start(Path.Combine(Target,"ClearDesk.exe"));f.Close();}catch(Exception ex){MessageBox.Show(ex.Message,"Ошибка установки");}};f.Controls.Add(b);Application.Run(f);
 }catch(Exception ex){MessageBox.Show(ex.ToString(),"ClearDesk Setup");Environment.ExitCode=1;}}
 static string Quote(string s){return "'"+s.Replace("'","''")+"'";}
 static void Install(){
  foreach(var p in Process.GetProcessesByName("ClearDesk"))using(p){try{if(String.Equals(p.MainModule.FileName,Path.Combine(Target,"ClearDesk.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Закройте ClearDesk перед установкой обновления.");}catch(System.ComponentModel.Win32Exception){}}
  Directory.CreateDirectory(Target);string exe=Path.Combine(Target,"ClearDesk.exe");using(var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("ClearDesk.exe"))using(var output=File.Create(exe))input.CopyTo(output);
  File.WriteAllText(Path.Combine(Target,"LICENSE.txt"),ProductInfo.License,new UTF8Encoding(true));
  string desktop=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"ClearDesk.lnk");string start=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"ClearDesk.lnk");
  string uninstall=Path.Combine(Target,"Uninstall.ps1");
  string script="$ErrorActionPreference = 'Stop'\r\nAdd-Type -AssemblyName System.Windows.Forms\r\nif ([System.Windows.Forms.MessageBox]::Show('Удалить ClearDesk? Резервные копии реестра сохранятся.', 'ClearDesk', 'YesNo') -ne 'Yes') { exit }\r\n"+
   "$target = "+Quote(Target)+"\r\n$expected = Join-Path $env:LOCALAPPDATA 'Programs\\ClearDesk'\r\nif ([IO.Path]::GetFullPath($target) -ne [IO.Path]::GetFullPath($expected)) { throw 'Unexpected uninstall path' }\r\n"+
   "Get-Process ClearDesk -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $target 'ClearDesk.exe') } | ForEach-Object { if (-not $_.CloseMainWindow()) { throw 'Закройте ClearDesk перед удалением.' }; if (-not $_.WaitForExit(5000)) { throw 'Закройте ClearDesk перед удалением.' } }\r\n"+
   "foreach ($shortcut in @("+Quote(desktop)+", "+Quote(start)+")) { if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force } }\r\nRemove-Item -LiteralPath 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\ClearDesk' -ErrorAction SilentlyContinue\r\n"+
   "foreach ($name in @('ClearDesk.exe','Uninstall.ps1','LICENSE.txt')) { $item = Join-Path $target $name; if (Test-Path -LiteralPath $item) { Remove-Item -LiteralPath $item -Force } }\r\nif (@(Get-ChildItem -LiteralPath $target -Force).Count -eq 0) { Remove-Item -LiteralPath $target }\r\n[System.Windows.Forms.MessageBox]::Show('ClearDesk удалён.', 'ClearDesk') | Out-Null\r\n";
  File.WriteAllText(uninstall,script,new UTF8Encoding(true));
  string shortcutScript="$w = New-Object -ComObject WScript.Shell\r\nforeach ($path in @("+Quote(desktop)+", "+Quote(start)+")) { $s = $w.CreateShortcut($path); $s.TargetPath = "+Quote(exe)+"; $s.WorkingDirectory = "+Quote(Target)+"; $s.Save() }";
  string encoded=Convert.ToBase64String(Encoding.Unicode.GetBytes(shortcutScript));using(var p=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+encoded){UseShellExecute=false,CreateNoWindow=true})){p.WaitForExit();if(p.ExitCode!=0)throw new IOException("Не удалось создать ярлыки.");}
  using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClearDesk")){key.SetValue("DisplayName","ClearDesk");key.SetValue("DisplayVersion",ProductInfo.Version);key.SetValue("Publisher",ProductInfo.Developer);key.SetValue("HelpLink","mailto:"+ProductInfo.Email);key.SetValue("InstallLocation",Target);key.SetValue("DisplayIcon",exe);key.SetValue("UninstallString","powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+uninstall+"\"");key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
 }
}
