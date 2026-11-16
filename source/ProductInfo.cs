using System;
using System.IO;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;

[assembly: AssemblyTitle("ClearDesk")]
[assembly: AssemblyDescription("Очистка и мониторинг Windows. Разработчик: Sever979.")]
[assembly: AssemblyCompany("Sever979")]
[assembly: AssemblyProduct("ClearDesk")]
[assembly: AssemblyCopyright("© 2026 Sever979. Все права защищены.")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]
[assembly: AssemblyInformationalVersion("1.1.1")]

internal static class ProductInfo {
 public const string Version="1.1.1";
 public const string Developer="Sever979";
 public const string Copyright="© 2026 Sever979. Все права защищены.";
 public const string Email="severinovdimka25@gmail.com";
 public static string License { get { using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("LICENSE.txt"))using(var reader=new StreamReader(stream)){return reader.ReadToEnd();} } }
 public static void ShowLicense(){using(var form=new Form{Text="ClearDesk — условия использования",Size=new Size(800,650),MinimumSize=new Size(600,400),StartPosition=FormStartPosition.CenterParent}){
  form.Controls.Add(new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Font=new Font("Segoe UI",11),Text=License});form.ShowDialog();
 }}
}
