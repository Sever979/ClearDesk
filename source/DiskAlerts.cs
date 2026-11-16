using System;
using System.Globalization;
using System.IO;

namespace ClearDesk {
// Settings and notification policy are separate from the UI so they can be tested without filling a disk.
internal sealed class DiskAlerts {
 public bool Enabled=true;
 public int ThresholdGb=10;
 public DateTime LastNotificationUtc=DateTime.MinValue;
 public const long GiB=1024L*1024L*1024L;
 public static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ClearDesk","disk-alerts.txt"); } }
 public bool IsLow(long availableBytes){return availableBytes>=0&&availableBytes<ThresholdGb*GiB;}
 public bool ShouldNotify(long availableBytes,DateTime now){
  return Enabled&&IsLow(availableBytes)&&(LastNotificationUtc==DateTime.MinValue||now-LastNotificationUtc>=TimeSpan.FromHours(6));
 }
 public static DiskAlerts Load(string path){
  var settings=new DiskAlerts();
  try{var lines=File.ReadAllLines(path);bool enabled;int threshold;long ticks;
   if(lines.Length!=3||!Boolean.TryParse(lines[0],out enabled)||!Int32.TryParse(lines[1],out threshold)||threshold<1||threshold>100||!Int64.TryParse(lines[2],out ticks)||ticks<0||ticks>DateTime.MaxValue.Ticks)return settings;
   settings.Enabled=enabled;settings.ThresholdGb=threshold;settings.LastNotificationUtc=new DateTime(ticks,DateTimeKind.Utc);
   if(settings.LastNotificationUtc>DateTime.UtcNow)settings.LastNotificationUtc=DateTime.MinValue;
  }catch(IOException){}catch(UnauthorizedAccessException){}
  return settings;
 }
 public void Save(string path){
  Directory.CreateDirectory(Path.GetDirectoryName(path));
  string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try{File.WriteAllLines(temporary,new[]{Enabled.ToString(),ThresholdGb.ToString(CultureInfo.InvariantCulture),LastNotificationUtc.Ticks.ToString(CultureInfo.InvariantCulture)});
   if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
  }finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
 public static void Test(string directory){
  var policy=new DiskAlerts();var now=new DateTime(2026,11,17,0,0,0,DateTimeKind.Utc);
  if(policy.ShouldNotify(10*GiB,now)||policy.ShouldNotify(-1,now)||!policy.ShouldNotify(9*GiB,now))throw new Exception("Disk alert threshold failed");
  policy.LastNotificationUtc=now;
  if(policy.ShouldNotify(GiB,now.AddHours(6).AddSeconds(-1))||!policy.ShouldNotify(GiB,now.AddHours(6)))throw new Exception("Disk alert cooldown failed");
  policy.Enabled=false;if(policy.ShouldNotify(0,now.AddDays(1)))throw new Exception("Disabled disk alert fired");
  string path=Path.Combine(directory,"disk-alert-test.txt");policy.ThresholdGb=20;policy.LastNotificationUtc=DateTime.UtcNow.AddHours(-1);policy.Save(path);
  var restored=Load(path);if(restored.Enabled||restored.ThresholdGb!=20||restored.LastNotificationUtc!=policy.LastNotificationUtc)throw new Exception("Disk alert settings persistence failed");
  restored.Enabled=true;restored.Save(path);if(Load(path).ShouldNotify(GiB,DateTime.UtcNow))throw new Exception("Restart cooldown failed");
  File.WriteAllText(path,"invalid settings");if(Load(path).ThresholdGb!=10||!Load(path).Enabled)throw new Exception("Settings recovery failed");File.Delete(path);
 }
}
}
