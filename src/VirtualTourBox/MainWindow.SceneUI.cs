using System.IO;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VirtualTourBox.Models;
using VirtualTourBox.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using MessageBox = System.Windows.MessageBox;

namespace VirtualTourBox;

public partial class MainWindow
{
    readonly Dictionary<string, Button> controllerButtons = new();
    readonly HashSet<string> highlightedInputs = new();
    readonly Dictionary<string,long> inputLastSeen = new();

    void ControllerLayout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (initializing || !IsLoaded || ControllerLayoutCombo.SelectedItem is not ComboBoxItem item) return;
        string previous = settings.ControllerLayout;
        settings.ControllerLayout = item.Content.ToString() ?? "Nintendo";
        try { SettingsStore.Save(settings); }
        catch (Exception ex)
        {
            settings.ControllerLayout = previous;
            initializing = true;
            ControllerLayoutCombo.SelectedIndex = previous switch { "Xbox" => 1, "PlayStation" => 2, _ => 0 };
            initializing = false;
            MessageBox.Show("布局保存失败：" + ex.Message);
            return;
        }
        BindingRow.ControllerLayout = settings.ControllerLayout;
        foreach (var row in rows) row.RefreshLayout();
        BuildControllerDiagram();
    }

    void NewSceneDialog_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveUnsaved()) return;
        var dialog = new Window { Title = "新建场景", Owner = this, Width = 410, Height = 260,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(22) };
        var name = new TextBox { Text = "我的场景", Margin = new Thickness(0,8,0,12) };
        var target = new ComboBox { ItemsSource = new[]{"Windows","iPad","原生游戏"}, SelectedIndex = 0 };
        var create = new Button { Content = "创建", Margin = new Thickness(0,16,0,0) };
        panel.Children.Add(new TextBlock { Text = "场景名称" }); panel.Children.Add(name);
        panel.Children.Add(target); panel.Children.Add(create); dialog.Content = panel;
        create.Click += (_,_) =>
        {
            string safe = ProfileStore.Sanitize(name.Text);
            if (store.Names().Contains(safe,StringComparer.OrdinalIgnoreCase)) { MessageBox.Show("名称已存在"); return; }
            try
            {
                var profile=target.SelectedIndex==1?ProfileStore.ProcreateDefault():new MappingProfile {Target=target.SelectedIndex==2?"Game":"Windows"};
                profile.Name=safe;store.Save(profile);
                dialog.DialogResult = true;
                RefreshProfiles(safe);
            }
            catch(Exception ex) { MessageBox.Show("新建失败：" + ex.Message); }
        };
        dialog.ShowDialog();
    }

    void DeleteScene_Click(object sender, RoutedEventArgs e)
    {
        if (currentProfile is null || !ResolveUnsaved()) return;
        if (store.Names().Count <= 1) { MessageBox.Show("请至少保留一个场景"); return; }
        if (MessageBox.Show("删除场景“" + currentProfile.Name + "”？删除前将备份全部配置。",
            "删除场景",MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        engine.Enabled = false; engine.Reset();
        try
        {
            ConfigurationBackup.Export(Path.Combine(ConfigurationBackup.Root,"before-delete-"+Guid.NewGuid().ToString("N")+".zip"));
            File.Delete(Path.Combine(ProfileStore.DirectoryPath,ProfileStore.Sanitize(currentProfile.Name)+".json"));
            currentProfile = null; engine.Profile = null;
            RefreshProfiles(); LoadSelectedProfile();
        }
        catch(Exception ex) { MessageBox.Show("删除失败："+ex.Message); }
    }

    void ExportScene_Click(object sender, RoutedEventArgs e)
    {
        if (currentProfile is null || !ResolveUnsaved()) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter="场景配置|*.json", FileName=ProfileStore.Sanitize(currentProfile.Name)+".json" };
        if(dialog.ShowDialog(this)!=true) return;
        try { File.WriteAllText(dialog.FileName,JsonSerializer.Serialize(currentProfile,new JsonSerializerOptions {WriteIndented=true})); }
        catch(Exception ex) { MessageBox.Show("导出失败："+ex.Message); }
    }

    void ImportScene_Click(object sender, RoutedEventArgs e)
    {
        if (!ResolveUnsaved()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter="场景配置|*.json" };
        if(dialog.ShowDialog(this)!=true) return;
        try
        {
            if(new FileInfo(dialog.FileName).Length>2_000_000) throw new InvalidDataException("场景文件过大");
            var profile=JsonSerializer.Deserialize<MappingProfile>(File.ReadAllText(dialog.FileName),
                new JsonSerializerOptions {PropertyNameCaseInsensitive=true}) ?? throw new InvalidDataException("场景为空");
            if(profile.SchemaVersion!=1 || profile.Target is not ("Windows" or "iPad" or "Game") ||
                profile.Bindings is null || profile.Bindings.Any(pair => pair.Value is null))
                throw new InvalidDataException("场景版本或内容无效");
            string baseName=ProfileStore.Sanitize(profile.Name);
            string name=baseName;
            int count=1;
            while(store.Names().Contains(name,StringComparer.OrdinalIgnoreCase)) name=baseName+" 导入"+count++;
            profile.Name=name;
            store.Save(profile);
            RefreshProfiles(name);
            OutputStatus.Text="已导入场景："+name;
        }
        catch(Exception ex) { MessageBox.Show("导入失败："+ex.Message); }
    }
}







