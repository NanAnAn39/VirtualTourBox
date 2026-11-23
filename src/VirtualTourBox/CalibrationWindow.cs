using System.Windows;
using System.Windows.Controls;
using VirtualTourBox.Services;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;

namespace VirtualTourBox;

public sealed class CalibrationWindow : Window
{
    public StickCalibration? Result { get; private set; }
    readonly StickCalibration original;
    readonly Dictionary<string, TextBox> numbers = new();
    readonly Dictionary<string, ComboBox> axes = new();
    readonly Dictionary<string, CheckBox> flips = new();

    public CalibrationWindow(StickCalibration settings)
    {
        original = settings;
        Title = "设备校准"; Width = 620; Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(20) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(new TextBlock { Text = "按键号为输入监视器后端的1起始编号。修改前记录原值。\n轴范围默认0–65535；同名同后端设备共用校准。", TextWrapping = TextWrapping.Wrap });
        foreach (var entry in settings.Buttons)
            AddNumber(panel, "Button."+entry.Key, entry.Key+" 按键号（1–32）", entry.Value);
        foreach (var name in new[]{"LeftXAxis","LeftYAxis","RightXAxis","RightYAxis"})
        {
            panel.Children.Add(new TextBlock { Text = name switch {
                "LeftXAxis" => "左摇杆水平轴", "LeftYAxis" => "左摇杆垂直轴",
                "RightXAxis" => "右摇杆水平轴", _ => "右摇杆垂直轴" } });
            var combo = new ComboBox { ItemsSource = new[]{"Auto","X","Y","Z","R","U","V"},
                SelectedItem = (string)typeof(StickCalibration).GetProperty(name)!.GetValue(settings)! };
            axes[name] = combo; panel.Children.Add(combo);
        }
        foreach (var item in new[]{("InvertLeftX","左水平反向"),("InvertLeftY","左垂直反向"),("InvertRightX","右水平反向"),("InvertRightY","右垂直反向")})
        {
            var check = new CheckBox { Content = item.Item2, IsChecked = (bool)typeof(StickCalibration).GetProperty(item.Item1)!.GetValue(settings)! };
            flips[item.Item1] = check; panel.Children.Add(check);
        }
        foreach (var item in new[]{("AxisMinimum","轴最小值"),("AxisMaximum","轴最大值"),
            ("PovRotationDegrees","十字键方向偏转角度（0/90/180/270）"),
            ("SharedZLow","WinMM共用Z低阈值"),("SharedZHigh","WinMM共用Z高阈值"),
            ("TriggerThreshold","XInput扳机阈值（0–65535）")})
            AddNumber(panel,item.Item1,item.Item2,Convert.ToInt64(typeof(StickCalibration).GetProperty(item.Item1)!.GetValue(settings)));
        var save = new Button { Content = "保存校准", Margin = new Thickness(0,15,0,0) };
        save.Click += Save; panel.Children.Add(save);
    }

    void AddNumber(StackPanel panel, string key, string label, long value)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0,6,0,0) });
        var box = new TextBox { Text = value.ToString() };
        numbers[key] = box; panel.Children.Add(box);
    }

    void Save(object sender, RoutedEventArgs e)
    {
        var result = System.Text.Json.JsonSerializer.Deserialize<StickCalibration>(
            System.Text.Json.JsonSerializer.Serialize(original))!;
        foreach (var entry in numbers)
        {
            if (!int.TryParse(entry.Value.Text, out int value) || value < 0 || value > 65535)
            { MessageBox.Show("数值必须为0–65535之间的整数"); return; }
            if (entry.Key.StartsWith("Button."))
            {
                if (value < 1 || value > 32) { MessageBox.Show("按键号必须为1–32"); return; }
                result.Buttons[entry.Key[7..]] = value;
            }
            else
            {
                var property = typeof(StickCalibration).GetProperty(entry.Key)!;
                object converted = property.PropertyType == typeof(uint) ? (object)(uint)value : value;
                property.SetValue(result, converted);
            }
        }
        if (result.Buttons.Values.Distinct().Count() != result.Buttons.Count)
        { MessageBox.Show("按键号不可重复"); return; }
        if (result.AxisMinimum >= result.AxisMaximum || result.SharedZLow >= result.SharedZHigh ||
            result.PovRotationDegrees % 90 != 0 || result.PovRotationDegrees > 270)
        { MessageBox.Show("请检查轴范围、Z阈值与方向角度"); return; }
        foreach (var entry in axes)
            typeof(StickCalibration).GetProperty(entry.Key)!.SetValue(result, entry.Value.SelectedItem);
        foreach (var entry in flips)
            typeof(StickCalibration).GetProperty(entry.Key)!.SetValue(result, entry.Value.IsChecked == true);
        Result = result;
        DialogResult = true;
    }
}


