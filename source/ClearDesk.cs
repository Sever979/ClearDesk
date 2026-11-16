using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClearDesk {
static class Program {
 [STAThread] static void Main(string[] args) {
  Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
  if(args.Contains("--test")){ Environment.Exit(Tests.Run()); return; }
  if(args.Contains("--registry-test")){ Environment.Exit(Tests.RegistryTest()); return; }
  Application.Run(new MainForm());
 }
}
static class Clean {
 public static bool Safe(string path,string root) {
  try { path=Path.GetFullPath(path); root=Path.GetFullPath(root).TrimEnd('\\');
   if(!path.StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase)) return false;
   string p=path; while(p!=null) { if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)return false; p=Path.GetDirectoryName(p); }
   return true;
  } catch { return false; }
 }
 public static List<TempItem> Scan(string root) {
  var result=new List<TempItem>(); var pending=new Stack<string>(); pending.Push(root);
  while(pending.Count>0) { string d=pending.Pop(); try {
   foreach(string sub in Directory.GetDirectories(d)) if(Safe(sub,root))pending.Push(sub);
   foreach(string f in Directory.GetFiles(d)) { if(!Safe(f,root))continue; var info=new FileInfo(f);
    if(info.LastWriteTimeUtc<DateTime.UtcNow.AddDays(-2))result.Add(new TempItem {Path=f,Root=root,Size=info.Length,Write=info.LastWriteTimeUtc}); }
  } catch(UnauthorizedAccessException){} catch(IOException){} }
  return result;
 }
 public static bool Delete(TempItem item) {
  if(!Safe(item.Path,item.Root))return false; var f=new FileInfo(item.Path);
  if(!f.Exists||f.Length!=item.Size||f.LastWriteTimeUtc!=item.Write||f.LastWriteTimeUtc>=DateTime.UtcNow.AddDays(-2))return false;
  f.Delete(); return true;
 }
 public static string Size(double n) { string[] u={"Б","КБ","МБ","ГБ","ТБ"}; int i=0;while(n>=1024&&i<4){n/=1024;i++;}return n.ToString("0.0")+" "+u[i]; }
}
class TempItem { public string Path,Root; public long Size; public DateTime Write; }
class RegItem { public RegistryHive Hive; public RegistryView View; public string Sub,Name,Command,Location; public string Full {get{return (Hive==RegistryHive.CurrentUser?"HKEY_CURRENT_USER":"HKEY_LOCAL_MACHINE")+"\\"+Sub;}} }
static class RegistryCleaner {
 const string Base=@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
 public static string Exe(string command) {
  if(String.IsNullOrWhiteSpace(command))return null; string c=Environment.ExpandEnvironmentVariables(command.Trim());
  if(!c.StartsWith("\""))return null; int end=c.IndexOf('"',1); if(end<2)return null;
  string p=c.Substring(1,end-1); if(!Path.IsPathRooted(p)||!p.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))return null;
  return p;
 }
 public static bool Orphan(string command,string location) {
  string exe=Exe(command); if(exe==null||String.IsNullOrWhiteSpace(location))return false;
  location=Environment.ExpandEnvironmentVariables(location.Trim().Trim('"'));
  if(!Path.IsPathRooted(location)||exe.StartsWith(@"\\")||location.StartsWith(@"\\"))return false;
  if(!String.Equals(Path.GetPathRoot(exe),Path.GetPathRoot(Environment.SystemDirectory),StringComparison.OrdinalIgnoreCase)||!String.Equals(Path.GetPathRoot(location),Path.GetPathRoot(Environment.SystemDirectory),StringComparison.OrdinalIgnoreCase))return false;
  try { if(!Path.GetFullPath(exe).StartsWith(Path.GetFullPath(location).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))return false; }catch{return false;}
  return Missing(exe)&&Missing(location);
 }
 static bool Missing(string path){try{File.GetAttributes(path);return false;}catch(FileNotFoundException){return true;}catch(DirectoryNotFoundException){return true;}catch{return false;}}
 public static List<RegItem> Scan() {
  var list=new List<RegItem>(); foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
   using(var root=RegistryKey.OpenBaseKey(hive,view))using(var parent=root.OpenSubKey(Base)) { if(parent==null)continue;
    foreach(string name in parent.GetSubKeyNames())try {using(var k=parent.OpenSubKey(name)) {
     string title=k.GetValue("DisplayName") as string, cmd=k.GetValue("UninstallString") as string, loc=k.GetValue("InstallLocation") as string;
     if(String.IsNullOrEmpty(title)||Convert.ToInt32(k.GetValue("SystemComponent",0))!=0||Convert.ToInt32(k.GetValue("WindowsInstaller",0))!=0||k.GetValue("ParentKeyName")!=null)continue;
     if(Orphan(cmd,loc))list.Add(new RegItem{Hive=hive,View=view,Sub=Base+"\\"+name,Name=title,Command=cmd,Location=loc});
    }}catch {} }
  } return list;
 }
 public static string Remove(RegItem item) {
  using(var root=RegistryKey.OpenBaseKey(item.Hive,item.View)) {
   using(var k=root.OpenSubKey(item.Sub)) { if(k==null || (string)k.GetValue("UninstallString")!=item.Command || (string)k.GetValue("InstallLocation")!=item.Location || !Orphan(item.Command,item.Location))throw new IOException("Запись изменилась: выполните поиск заново."); }
   string dir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearDeskBackups"); Directory.CreateDirectory(dir);
   string file=Path.Combine(dir,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+"-"+(item.View==RegistryView.Registry64?"64":"32")+".reg");
   var start=new ProcessStartInfo("reg.exe","export \""+item.Full+"\" \""+file+"\" /y /reg:"+(item.View==RegistryView.Registry64?"64":"32")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
   using(var p=Process.Start(start)){p.WaitForExit();if(p.ExitCode!=0||!File.Exists(file)||new FileInfo(file).Length==0)throw new IOException("Не удалось создать резервную копию.");}
   root.DeleteSubKeyTree(item.Sub); return file;
  }
 }
}
class Chart:Control {
 public List<float> Values=new List<float>(); public Chart(){DoubleBuffered=true;BackColor=Color.FromArgb(24,35,53);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var grid=new Pen(Color.FromArgb(42,57,77)))for(int y=20;y<Height;y+=30)e.Graphics.DrawLine(grid,0,y,Width,y);
  if(Values.Count<2)return;var pts=Values.Select((v,i)=>new PointF(i*Width/59f,Height-8-v*(Height-16)/100f)).ToArray();using(var p=new Pen(Color.FromArgb(53,211,177),3))e.Graphics.DrawLines(p,pts);
 }
}
class MainForm:Form {
 [StructLayout(LayoutKind.Sequential)] struct Mem {public uint Length,Load;public ulong Total,Avail,PageTotal,PageAvail,VirtualTotal,VirtualAvail,Extended;}
 [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref Mem m);
 [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out long idle,out long kernel,out long user);
 long prevIdle,prevTotal,prevRx,prevTx;DateTime networkTime=DateTime.UtcNow; bool sampled;
 Label metrics=new Label(),disks=new Label(),status=new Label(); Chart chart=new Chart(); ListView processes,files,registry; Button scanTemp,cleanTemp,scanReg,cleanReg; System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer(); NotifyIcon tray;
 public MainForm(){
  Text="ClearDesk — очистка и мониторинг"; Size=new Size(1100,760); MinimumSize=new Size(940,650); StartPosition=FormStartPosition.CenterScreen; Font=new Font("Segoe UI",10); BackColor=Color.FromArgb(14,23,38); ForeColor=Color.White;
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(22)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,35));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(layout);
  layout.Controls.Add(new Label{Text="ClearDesk   /   забота о системе",Font=new Font("Segoe UI",23,FontStyle.Bold),AutoSize=true},0,0);
  layout.Controls.Add(new Label{Text="Локально • Без передачи данных • Очистка только по вашему выбору",AutoSize=true,ForeColor=Color.LightSteelBlue},0,1);
  var tabs=new TabControl{Dock=DockStyle.Fill};layout.Controls.Add(tabs,0,2);status.Dock=DockStyle.Fill;status.Text="Готово. Мониторинг обновляется каждые 2 секунды.";status.Padding=new Padding(0,10,0,0);layout.Controls.Add(status,0,3);
  var monitor=Page(tabs,"Мониторинг");var ml=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=4,ColumnCount=1,Padding=new Padding(14)};foreach(int h in new[]{72,130,64})ml.RowStyles.Add(new RowStyle(SizeType.Absolute,h));ml.RowStyles.Add(new RowStyle(SizeType.Percent,100));monitor.Controls.Add(ml);
  metrics.Dock=DockStyle.Fill;metrics.Font=new Font("Segoe UI",15,FontStyle.Bold);ml.Controls.Add(metrics);chart.Dock=DockStyle.Fill;ml.Controls.Add(chart);disks.Dock=DockStyle.Fill;ml.Controls.Add(disks);processes=List(new[]{"Процесс","PID","Память"});ml.Controls.Add(processes);
  var temp=Page(tabs,"Временные файлы");files=List(new[]{"Файл","Размер","Изменён"});files.CheckBoxes=true;
  var tl=Content(temp,"Временные файлы пользователя старше 48 часов. Проверьте список: удаление без корзины.",files);
  scanTemp=Button("Найти файлы",async delegate {await ScanTemp();});cleanTemp=Button("Удалить выбранные",async delegate {await DeleteTemp();});tl.Controls.Add(scanTemp);tl.Controls.Add(Button("Выбрать все",delegate{foreach(ListViewItem i in files.Items)i.Checked=true;}));tl.Controls.Add(Button("Снять выбор",delegate{foreach(ListViewItem i in files.Items)i.Checked=false;}));tl.Controls.Add(cleanTemp);
  var reg=Page(tabs,"Остатки в реестре");registry=List(new[]{"Программа","Раздел / разрядность","Путь установки"});registry.CheckBoxes=true;
  var rl=Content(reg,"Кандидаты: отсутствуют папка установки и деинсталлятор. Перед удалением сохраняется .reg-копия.",registry);
  scanReg=Button("Найти записи",async delegate{await ScanReg();});cleanReg=Button("Удалить выбранные",async delegate{await DeleteReg();});rl.Controls.Add(scanReg);rl.Controls.Add(cleanReg);
  rl.Controls.Add(Button("Резервные копии",delegate{string p=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearDeskBackups");Directory.CreateDirectory(p);Process.Start("explorer.exe",p);}));
  rl.Controls.Add(Button("От администратора",delegate{try{Process.Start(new ProcessStartInfo(Application.ExecutablePath){UseShellExecute=true,Verb="runas"});Close();}catch(Exception ex){status.Text=ex.Message;}}));
  var help=Page(tabs,"Справка");help.Controls.Add(new Label{Dock=DockStyle.Fill,Padding=new Padding(20),Text="ClearDesk 1.1\n\nОчистка файлов: только пользовательская папка Temp, файлы старше 48 часов.\nНедоступные, изменённые файлы и ссылки пропускаются. Все флажки по умолчанию сняты.\n\nРеестр: только записи списка установленных программ HKCU/HKLM, 32 и 64 бита.\nMSI, системные компоненты и неоднозначные команды не затрагиваются.\nОтсутствующая папка может означать перенос приложения — проверяйте каждый кандидат.\nУдаление записи не ускоряет компьютер и не является удалением самой программы.\nДля HKLM нужен запуск от администратора.\n\nКопии реестра: %LOCALAPPDATA%\\ClearDeskBackups.\nВосстановление: reg import «файл.reg» /reg:64 или /reg:32 согласно списку записей.\nДля HKLM запускайте терминал от администратора. Копии сохраняются после удаления ClearDesk.\n\nСверните окно для мониторинга в трее. Закрытие окна завершает приложение.\nCPU: загрузка всей системы. Память процессов: рабочий набор.\nСеть: суммарный трафик активных сетевых интерфейсов.\nПрограмма не отправляет данные и не запускает очистку по расписанию.",ForeColor=Color.White});
  var about=Page(tabs,"О программе");
  var aboutLayout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(18)};aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent,100));aboutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));about.Controls.Add(aboutLayout);
  aboutLayout.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,BorderStyle=BorderStyle.None,BackColor=about.BackColor,ForeColor=Color.White,Font=new Font("Segoe UI",11),Text=
   "ClearDesk "+ProductInfo.Version+" — Free\r\nРазработчик: "+ProductInfo.Developer+"\r\n"+ProductInfo.Copyright+"\r\nПочта: "+ProductInfo.Email+
   "\r\n\r\nУсловно-бесплатная программа\r\nБазовая редакция бесплатна без ограничения срока. Мониторинг, ручная очистка временных файлов и остатков в реестре, резервные копии доступны сейчас.\r\n\r\nClearDesk Pro — планируемая ежемесячная подписка\r\n• Очистка по расписанию с исключениями.\r\n• История нагрузки за 30 дней и отчёты CSV.\r\n• Уведомления о нагрузке и нехватке свободного места.\r\n• Поиск крупных файлов и дубликатов с ручным выбором.\r\n• Управление автозагрузкой с отменой изменений.\r\n• Настраиваемые профили очистки.\r\n\r\nPro пока не реализован и не продаётся. Цена и условия будут опубликованы перед запуском. Оплата и автоматические списания отсутствуют.\r\nБесплатные функции и резервные копии сохранятся после окончания будущей подписки."});
  var aboutButtons=new FlowLayoutPanel{Dock=DockStyle.Fill};aboutLayout.Controls.Add(aboutButtons);aboutButtons.Controls.Add(Button("Условия использования",delegate{ProductInfo.ShowLicense();}));aboutButtons.Controls.Add(Button("Написать разработчику",delegate{try{Process.Start(new ProcessStartInfo("mailto:"+ProductInfo.Email+"?subject=ClearDesk"){UseShellExecute=true});}catch{MessageBox.Show("Напишите на "+ProductInfo.Email);}}));
  tray=new NotifyIcon{Icon=SystemIcons.Application,Text="ClearDesk",Visible=true};tray.DoubleClick+=delegate{Show();WindowState=FormWindowState.Normal;Activate();};var menu=new ContextMenuStrip();menu.Items.Add("Открыть",null,delegate{Show();WindowState=FormWindowState.Normal;});menu.Items.Add("Выход",null,delegate{Close();});tray.ContextMenuStrip=menu;
  Resize+=delegate{if(WindowState==FormWindowState.Minimized)Hide();};FormClosed+=delegate{timer.Stop();tray.Dispose();};timer.Interval=2000;timer.Tick+=delegate{UpdateMetrics();};UpdateMetrics();timer.Start();
 }
 TabPage Page(TabControl tabs,string title){var p=new TabPage(title){BackColor=Color.FromArgb(24,35,53),ForeColor=Color.White};tabs.TabPages.Add(p);return p;}
 ListView List(string[] names){var l=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,BackColor=Color.FromArgb(20,30,46),ForeColor=Color.White,BorderStyle=BorderStyle.None};foreach(var n in names)l.Columns.Add(n,200);l.Columns[0].Width=400;return l;}
 FlowLayoutPanel Content(TabPage page,string hint,ListView list){var t=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(12)};t.RowStyles.Add(new RowStyle(SizeType.Absolute,55));t.RowStyles.Add(new RowStyle(SizeType.Percent,100));t.RowStyles.Add(new RowStyle(SizeType.Absolute,55));t.Controls.Add(new Label{Text=hint,Dock=DockStyle.Fill});t.Controls.Add(list);var f=new FlowLayoutPanel{Dock=DockStyle.Fill};t.Controls.Add(f);page.Controls.Add(t);return f;}
 Button Button(string text,EventHandler action){var b=new Button{Text=text,AutoSize=true,Height=36,BackColor=Color.FromArgb(42,65,88),ForeColor=Color.White,FlatStyle=FlatStyle.Flat,Padding=new Padding(5),Margin=new Padding(3,10,5,0)};b.Click+=action;return b;}
 void UpdateMetrics(){try{
  long idle,kernel,user;double cpu=0;if(GetSystemTimes(out idle,out kernel,out user)){long total=kernel+user;if(sampled&&total>prevTotal)cpu=Math.Max(0,Math.Min(100,100.0*(total-prevTotal-idle+prevIdle)/(total-prevTotal)));prevIdle=idle;prevTotal=total;sampled=true;}
  var m=new Mem{Length=(uint)Marshal.SizeOf(typeof(Mem))};GlobalMemoryStatusEx(ref m);
  long rx=0,tx=0;foreach(var n in NetworkInterface.GetAllNetworkInterfaces())if(n.OperationalStatus==OperationalStatus.Up&&n.NetworkInterfaceType!=NetworkInterfaceType.Loopback){var s=n.GetIPv4Statistics();rx+=s.BytesReceived;tx+=s.BytesSent;}
  double sec=Math.Max(.1,(DateTime.UtcNow-networkTime).TotalSeconds);string net=prevRx==0?"измерение…":"↓ "+Clean.Size(Math.Max(0,rx-prevRx)/sec)+"/с    ↑ "+Clean.Size(Math.Max(0,tx-prevTx)/sec)+"/с";prevRx=rx;prevTx=tx;networkTime=DateTime.UtcNow;
  metrics.Text="CPU   "+cpu.ToString("0")+"%       RAM   "+m.Load+"%   ("+Clean.Size(m.Total-m.Avail)+" / "+Clean.Size(m.Total)+")\nСеть   "+net;
  chart.Values.Add((float)cpu);if(chart.Values.Count>60)chart.Values.RemoveAt(0);chart.Invalidate();
  disks.Text=String.Join("     ",DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed).Select(d=>d.Name+" свободно "+Clean.Size(d.AvailableFreeSpace)+" из "+Clean.Size(d.TotalSize)));
  var rows=new List<Tuple<string,int,long>>();foreach(var p in Process.GetProcesses())using(p)try{rows.Add(Tuple.Create(p.ProcessName,p.Id,p.WorkingSet64));}catch{}
  processes.BeginUpdate();processes.Items.Clear();foreach(var p in rows.OrderByDescending(x=>x.Item3).Take(25))processes.Items.Add(new ListViewItem(new[]{p.Item1,p.Item2.ToString(),Clean.Size(p.Item3)}));processes.EndUpdate();
 }catch(Exception ex){status.Text="Мониторинг: "+ex.Message;}}
 async Task ScanTemp(){scanTemp.Enabled=cleanTemp.Enabled=false;status.Text="Поиск временных файлов…";try{var list=await Task.Run(()=>Clean.Scan(Path.GetTempPath()));files.BeginUpdate();files.Items.Clear();foreach(var f in list)files.Items.Add(new ListViewItem(new[]{f.Path,Clean.Size(f.Size),f.Write.ToLocalTime().ToString()}){Tag=f});files.EndUpdate();status.Text="Найдено: "+list.Count+" файлов, "+Clean.Size(list.Sum(f=>f.Size))+". Выберите файлы для удаления.";}catch(Exception ex){status.Text=ex.Message;}finally{scanTemp.Enabled=cleanTemp.Enabled=true;}}
 async Task DeleteTemp(){var selected=files.CheckedItems.Cast<ListViewItem>().ToList();if(selected.Count==0){status.Text="Сначала выберите файлы.";return;}if(MessageBox.Show("Удалить выбранные файлы без возможности восстановления из корзины?\nФайлов: "+selected.Count,"Подтверждение очистки",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;scanTemp.Enabled=cleanTemp.Enabled=false;int count=0;long bytes=0;try{foreach(var row in selected){var f=(TempItem)row.Tag;bool ok=await Task.Run(()=>{try{return Clean.Delete(f);}catch{return false;}});if(ok){count++;bytes+=f.Size;files.Items.Remove(row);}}status.Text="Удалено: "+count+", освобождено "+Clean.Size(bytes)+". Пропущено: "+(selected.Count-count);}finally{scanTemp.Enabled=cleanTemp.Enabled=true;}}
 async Task ScanReg(){scanReg.Enabled=cleanReg.Enabled=false;status.Text="Проверка записей установленных программ…";try{var list=await Task.Run(()=>RegistryCleaner.Scan());registry.Items.Clear();foreach(var r in list)registry.Items.Add(new ListViewItem(new[]{r.Name,r.Full+" / "+(r.View==RegistryView.Registry64?"64":"32"),r.Location}){Tag=r});status.Text="Найдено кандидатов: "+list.Count+". Проверьте названия и пути перед выбором.";}catch(Exception ex){status.Text=ex.Message;}finally{scanReg.Enabled=cleanReg.Enabled=true;}}
 async Task DeleteReg(){var rows=registry.CheckedItems.Cast<ListViewItem>().ToList();if(rows.Count==0){status.Text="Сначала выберите записи.";return;}if(rows.Any(r=>((RegItem)r.Tag).Hive==RegistryHive.LocalMachine)&&!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)){MessageBox.Show("Для выбранных системных записей запустите ClearDesk от администратора.");return;}if(MessageBox.Show("Удалить выбранные записи списка программ?\nБудет создана .reg-копия каждой записи.\nКоличество: "+rows.Count,"Изменение реестра",MessageBoxButtons.YesNo,MessageBoxIcon.Warning)!=DialogResult.Yes)return;scanReg.Enabled=cleanReg.Enabled=false;int n=0;string err="";try{foreach(var row in rows)try{await Task.Run(()=>RegistryCleaner.Remove((RegItem)row.Tag));registry.Items.Remove(row);n++;}catch(Exception ex){err=ex.Message;}status.Text="Удалено записей: "+n+". Копии: кнопка «Резервные копии». "+err;}finally{scanReg.Enabled=cleanReg.Enabled=true;}}
}
static class Tests {
 public static int RegistryTest(){
  string sub=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ClearDesk-Test-"+Guid.NewGuid().ToString("N");
  string missing=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearDesk-Missing-"+Guid.NewGuid().ToString("N"));
  var item=new RegItem{Hive=RegistryHive.CurrentUser,View=RegistryView.Registry64,Sub=sub,Name="ClearDesk synthetic test",Location=missing,Command="\""+Path.Combine(missing,"uninstall.exe")+"\""};string backup=null;
  try{using(var root=RegistryKey.OpenBaseKey(item.Hive,item.View)){
   using(var k=root.CreateSubKey(sub)){k.SetValue("DisplayName",item.Name);k.SetValue("UninstallString",item.Command);k.SetValue("InstallLocation",item.Location);}
   if(!RegistryCleaner.Scan().Any(r=>r.Sub.Equals(sub,StringComparison.OrdinalIgnoreCase)))throw new Exception("Synthetic candidate not found");
   backup=RegistryCleaner.Remove(item);using(var k=root.OpenSubKey(sub)){if(k!=null)throw new Exception("Synthetic key not removed");}
   using(var p=Process.Start(new ProcessStartInfo("reg.exe","import \""+backup+"\" /reg:64"){UseShellExecute=false,CreateNoWindow=true})){p.WaitForExit();if(p.ExitCode!=0)throw new Exception("Restore failed");}
   using(var k=root.OpenSubKey(sub)){if(k==null||(string)k.GetValue("DisplayName")!=item.Name||(string)k.GetValue("UninstallString")!=item.Command)throw new Exception("Restored data mismatch");}
  }File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"registry-test-result.txt"),"PASS: synthetic HKCU candidate scanning, backup export, selected key deletion, 64-bit import and data verification.");return 0;
  }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"registry-test-result.txt"),ex.ToString());return 1;}
  finally{using(var root=RegistryKey.OpenBaseKey(item.Hive,item.View))root.DeleteSubKeyTree(sub,false);if(backup!=null&&File.Exists(backup))File.Delete(backup);}
 }
 public static int Run(){string root=Path.Combine(Path.GetTempPath(),"ClearDeskTest-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{
  string old=Path.Combine(root,"old.tmp"),fresh=Path.Combine(root,"fresh.tmp");File.WriteAllText(old,"test");File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-3));File.WriteAllText(fresh,"keep");
  var found=Clean.Scan(root);if(found.Count!=1)throw new Exception("age filter");File.AppendAllText(old,"changed");if(Clean.Delete(found[0]))throw new Exception("changed file deleted");found=Clean.Scan(root);if(found.Count!=0)throw new Exception("fresh changed file included");
  File.SetLastWriteTimeUtc(old,DateTime.UtcNow.AddDays(-3));found=Clean.Scan(root);if(!Clean.Delete(found[0])||!File.Exists(fresh))throw new Exception("delete scope");if(Clean.Safe(fresh,root+"-other"))throw new Exception("boundary");
  if(RegistryCleaner.Orphan("msiexec /x {fake}",root)||RegistryCleaner.Orphan("\""+Path.Combine(root,"no.exe")+"\"",root))throw new Exception("registry false positive");
  string missing=Path.Combine(root,"missing");if(!RegistryCleaner.Orphan("\""+Path.Combine(missing,"uninstall.exe")+"\"",missing))throw new Exception("orphan detection");
  using(var form=new MainForm()){form.Show();Application.DoEvents();var tabs=form.Controls[0].Controls.OfType<TabControl>().Single();for(int i=0;i<tabs.TabCount;i++){tabs.SelectedIndex=i;Application.DoEvents();using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(0,0,bmp.Width,bmp.Height));bmp.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,i==0?"preview.png":"preview-"+i+".png"));}}form.Close();}
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),"PASS: age filter; modified-file protection; deletion scope; path boundary; conservative registry detection; UI render.");return 0;
 }catch(Exception ex){File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"test-result.txt"),ex.ToString());return 1;}finally{foreach(string f in Directory.GetFiles(root))File.Delete(f);Directory.Delete(root);}}
}
}
