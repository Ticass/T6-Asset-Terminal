using System.Diagnostics;using System.IO;using System.Windows;using System.Windows.Media;using T6AssetTool.Core;using Forms=System.Windows.Forms;using OpenFileDialog=Microsoft.Win32.OpenFileDialog;using SaveFileDialog=Microsoft.Win32.SaveFileDialog;using MessageBox=System.Windows.MessageBox;using Brushes=System.Windows.Media.Brushes;using Color=System.Windows.Media.Color;
namespace T6AssetTool.Gui;

public partial class MainWindow:Window
{
 const string IPakFilter="Black Ops II Xbox IPAK|*.ipak";

 public MainWindow()
 {
  InitializeComponent();
  PickIPak.Click+=(_,_)=>PickPackage();
  PickOutput.Click+=(_,_)=>PickFolder();
  PickRepackOut.Click+=(_,_)=>PickRebuiltPackage();
  OpenOutput.Click+=(_,_)=>Reveal();
  ModeExtract.Checked+=(_,_)=>ApplyMode();
  ModeRepack.Checked+=(_,_)=>ApplyMode();
  Execute.Click+=async(_,_)=>await Run();
  ApplyMode();
 }

 bool Repacking=>ModeRepack.IsChecked==true;

 void ApplyMode()
 {
  bool repack=Repacking;
  FolderLabel.Content=repack?"03  MODIFIED DDS DIRECTORY":"03  CLEAN DDS OUTPUT DIRECTORY";
  RepackOutLabel.Visibility=RepackOutRow.Visibility=repack?Visibility.Visible:Visibility.Collapsed;
  Execute.Content=repack?"▶  EXECUTE REPACK":"▶  EXECUTE EXTRACTION";
  PolicyTitle.Text=repack?"REPACK POLICY":"OUTPUT POLICY";
  PolicyText.Text=repack
   ?"Every .dds in the directory replaces the entry whose name it matches; entries you did not touch are copied through byte for byte, so an untouched IPAK rebuilds identically. Name each file after the image it replaces."
   :"The IPAK is grouped into complete textures and converted directly to named DDS files. No fastfiles or material extraction required.";
 }

 void PickPackage(){var d=new OpenFileDialog{Filter=IPakFilter};if(d.ShowDialog()==true)IPak.Text=d.FileName;}

 void PickFolder()
 {
  using var d=new Forms.FolderBrowserDialog{Description=Repacking?"Select the directory of modified DDS files":"Select clean DDS output directory"};
  if(Directory.Exists(Output.Text))d.SelectedPath=Output.Text;
  if(d.ShowDialog()==Forms.DialogResult.OK)Output.Text=d.SelectedPath;
 }

 void PickRebuiltPackage()
 {
  var d=new SaveFileDialog{Filter=IPakFilter,FileName=Path.GetFileName(IPak.Text)};
  if(d.ShowDialog()==true)RepackOut.Text=d.FileName;
 }

 // In repack mode the interesting output is the rebuilt file, so reveal it in Explorer
 // rather than opening the DDS directory the user fed in.
 void Reveal()
 {
  if(Repacking&&File.Exists(RepackOut.Text)){Process.Start(new ProcessStartInfo("explorer.exe",$"/select,\"{RepackOut.Text}\""){UseShellExecute=true});return;}
  if(Directory.Exists(Output.Text))Process.Start(new ProcessStartInfo("explorer.exe",Output.Text){UseShellExecute=true});
 }

 async Task Run()
 {
  string ipakPath=IPak.Text,folder=Output.Text,rebuilt=RepackOut.Text;
  bool repack=Repacking;
  if(!File.Exists(ipakPath)){MessageBox.Show("Select a valid Xbox 360 BO2 IPAK.","Input required");return;}
  if(repack)
  {
   if(!Directory.Exists(folder)){MessageBox.Show("Select the directory holding your modified DDS files.","Input required");return;}
   if(string.IsNullOrWhiteSpace(rebuilt)){MessageBox.Show("Choose where to write the rebuilt IPAK.","Input required");return;}
   if(string.Equals(Path.GetFullPath(rebuilt),Path.GetFullPath(ipakPath),StringComparison.OrdinalIgnoreCase))
   {MessageBox.Show("The rebuilt IPAK must not overwrite the source package.","Input required");return;}
  }
  Execute.IsEnabled=false;Progress.IsIndeterminate=true;
  Status.Text="PROCESSING";Status.Foreground=Brushes.Orange;StatusDot.Fill=Brushes.Orange;
  Summary.Text=repack?"IPAK REPACK ACTIVE":"IPAK EXTRACTION ACTIVE";
  Log.Clear();
  try
  {
   var progress=new Progress<string>(s=>{Log.AppendText($"{DateTime.Now:HH:mm:ss}  {s}\r\n");Log.ScrollToEnd();});
   void Say(string s)=>((IProgress<string>)progress).Report(s);
   string summary;
   if(repack)
   {
    var r=await Task.Run(()=>{var swaps=IPakRepacker.FromFolder(folder,Say);return IPakRepacker.Repack(ipakPath,rebuilt,swaps,Say);});
    summary=$"{r.Replaced} REPLACED / {r.Entries} ENTRIES";
   }
   else
   {
    var r=await Task.Run(()=>AssetExtractor.RunIPak(ipakPath,folder,Say));
    summary=$"{r.Textures} DDS TEXTURES";
   }
   Status.Text="COMPLETE";Status.Foreground=new SolidColorBrush(Color.FromRgb(71,213,201));StatusDot.Fill=Status.Foreground;
   Summary.Text=summary;
  }
  catch(Exception e)
  {
   Status.Text="FAILED";Status.Foreground=Brushes.IndianRed;StatusDot.Fill=Brushes.IndianRed;
   Summary.Text="JOB FAILED";Log.AppendText("ERROR  "+e+"\r\n");
  }
  finally{Progress.IsIndeterminate=false;Execute.IsEnabled=true;}
 }
}
