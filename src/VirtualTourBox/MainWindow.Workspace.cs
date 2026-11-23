using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using VirtualTourBox.Models;
using VirtualTourBox.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
namespace VirtualTourBox;
public partial class MainWindow
{
    readonly Dictionary<string,Button> callouts = new();
    string? selectedInput;
    List<BindingRow>? inspectorDraft;
    bool InspectorDirty() => inspectorDraft?.Any(d => rows.FirstOrDefault(r=>r.Input==d.Input) is BindingRow r && (r.Action!=d.Action||r.Shortcut!=d.Shortcut)) == true;
    bool ResolveInspectorDraft()
    {
        if(!InspectorDirty())return true;
        return System.Windows.MessageBox.Show("放弃尚未保存的按键修改？","按键设置",MessageBoxButton.YesNo)==MessageBoxResult.Yes;
    }
    readonly DispatcherTimer toastTimer = new() { Interval=TimeSpan.FromSeconds(4) };
    static SolidColorBrush Ink(string hex) => new((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
    void ShowToast(string text)
    {
        ToastText.Text=text; ToastBox.Visibility=Visibility.Visible;
        toastTimer.Stop(); toastTimer.Tick -= HideToast; toastTimer.Tick += HideToast; toastTimer.Start();
    }
    void DismissToast_Click(object sender,RoutedEventArgs e) {toastTimer.Stop();ToastBox.Visibility=Visibility.Collapsed;}
    void HideToast(object? sender,EventArgs e) { toastTimer.Stop(); ToastBox.Visibility=Visibility.Collapsed; }
    void ShowSettings_Click(object sender,RoutedEventArgs e) => WorkspaceTabs.SelectedIndex=2;
    Button DiagramButton(string text,double x,double y,double w,double h)
    {
        var button=new Button {Content=text,Width=w,Height=h,Padding=new Thickness(3),FontSize=18,
            Background=Ink("#F0F1EE"),Foreground=Ink("#353A3D")};
        var border=new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty,new CornerRadius(h/2));
        border.SetValue(Border.BorderBrushProperty,Ink("#879095")); border.SetValue(Border.BorderThicknessProperty,new Thickness(1.3));
        border.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
        var presenter=new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);
        border.AppendChild(presenter); button.Template=new ControlTemplate(typeof(Button)){VisualTree=border};
        Canvas.SetLeft(button,x);Canvas.SetTop(button,y);ControllerCanvas.Children.Add(button);return button;
    }

    void SceneMenu_Click(object sender,RoutedEventArgs e)
    {
        var menu=new ContextMenu();
        foreach(var pair in new (string,RoutedEventHandler)[]{("新建场景",NewSceneDialog_Click),("删除场景",DeleteScene_Click),("导入场景",ImportScene_Click),("导出场景",ExportScene_Click)})
        {var item=new MenuItem {Header=pair.Item1};item.Click+=pair.Item2;menu.Items.Add(item);}
        menu.PlacementTarget=sender as UIElement;menu.IsOpen=true;
    }
    System.Windows.Shapes.Path Shape(string data,string fill,string stroke="#555B60",double thickness=2)
    {
        var shape=new System.Windows.Shapes.Path {Data=Geometry.Parse(data),Fill=Ink(fill),Stroke=Ink(stroke),StrokeThickness=thickness,IsHitTestVisible=false};
        ControllerCanvas.Children.Add(shape);return shape;
    }
    void BuildControllerDiagram()
    {
        ControllerCanvas.Children.Clear();controllerButtons.Clear();callouts.Clear();highlightedInputs.Clear();inputLastSeen.Clear();
        Shape("M 300,205 C 300,168 310,148 336,148 C 360,148 371,172 372,205 Z","#E4E4E1");
        Shape("M 668,205 C 669,172 680,148 704,148 C 730,148 740,168 740,205 Z","#E4E4E1");
        Shape("M 277,245 C 282,195 302,182 333,182 C 367,182 389,206 392,245 Z","#E9E9E6");
        Shape("M 648,245 C 651,206 673,182 707,182 C 738,182 758,195 763,245 Z","#E9E9E6");
        Shape("M 334,226 C 295,224 274,247 255,296 C 225,367 198,475 194,569 C 190,643 225,675 264,646 L 357,573 C 384,556 405,552 434,552 L 606,552 C 635,552 656,556 683,573 L 776,646 C 815,675 850,643 846,569 C 842,475 815,367 785,296 C 766,247 745,224 706,226 Z","#F0EFEC","#51565B",2.1);
        Shape("M 233,365 C 242,448 299,459 316,510 L 357,573 L 264,646 C 225,675 190,643 194,569 C 198,491 211,421 233,365 Z","#CDCDCA","#CDCDCA",0);
        Shape("M 807,365 C 798,448 741,459 724,510 L 683,573 L 776,646 C 815,675 850,643 846,569 C 842,491 829,421 807,365 Z","#CDCDCA","#CDCDCA",0);
        // Continuous d-pad silhouette. Individual directions remain independent hit targets.
        var dpad=Shape("M 310,310 L 333,310 Q 341,310 341,318 L 341,345 L 369,345 Q 377,345 377,353 L 377,378 Q 377,386 369,386 L 341,386 L 341,413 Q 341,421 333,421 L 310,421 Q 302,421 302,413 L 302,386 L 275,386 Q 267,386 267,378 L 267,353 Q 267,345 275,345 L 302,345 L 302,318 Q 302,310 310,310 Z","#E4E4E1");
        dpad.RenderTransform=new TranslateTransform(30,-25);
        foreach(var c in new[]{(x:400.0,y:485.0),(x:620.0,y:485.0)})
        {
            foreach(double radius in new[]{58.0,48.0,35.0})
            {var circle=new Ellipse{Width=radius*2,Height=radius*2,Stroke=Ink("#787D80"),StrokeThickness=1.5,Fill=radius==35?Ink("#BFC0BD"):Brushes.Transparent,IsHitTestVisible=false};Canvas.SetLeft(circle,c.x-radius);Canvas.SetTop(circle,c.y-radius);ControllerCanvas.Children.Add(circle);}
        }
        var positions=new (string key,double x,double y,double w,double h)[]{
            ("ZL",305,154,62,34),("L",298,194,75,34),("ZR",673,154,62,34),("R",667,194,75,34),
            ("DPadUp",333,286,37,35),("DPadLeft",298,321,35,39),("DPadRight",371,321,35,39),("DPadDown",333,361,37,34),
            ("L3",365,450,70,70),("R3",585,450,70,70),("Select",440,290,40,36),("Start",530,290,40,36),
            ("X",650,264,49,49),("Y",598,316,49,49),("A",702,316,49,49),("B",650,368,49,49)};
        foreach(var pos in positions)
        {
            string key=pos.key;
            if(settings.ControllerLayout!="Nintendo")key=key switch {"A"=>"B","B"=>"A","X"=>"Y","Y"=>"X",_=>key};
            var row=rows.FirstOrDefault(r=>r.Input==key);if(row is null)continue;
            string text=key switch {"DPadUp"=>"▴","DPadDown"=>"▾","DPadLeft"=>"◂","DPadRight"=>"▸","Select"=>"−","Start"=>"+","L3"=>"","R3"=>"",_=>BindingRow.InputLabel(key)};
            var button=DiagramButton(text,pos.x,pos.y,pos.w,pos.h);
            button.FontSize=key is "A" or "B" or "X" or "Y"?25:18;
            if(key.StartsWith("DPad")||key is "L3" or "R3" or "L" or "R" or "ZL" or "ZR")
            {
                var template=new FrameworkElementFactory(typeof(Border));
                template.SetValue(Border.CornerRadiusProperty,new CornerRadius(key.StartsWith("DPad")?4:30));
                template.SetValue(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=System.Windows.Data.RelativeSource.TemplatedParent});
                var presenter=new FrameworkElementFactory(typeof(ContentPresenter));presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty,System.Windows.HorizontalAlignment.Center);presenter.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);template.AppendChild(presenter);
                button.Template=new ControlTemplate(typeof(Button)){VisualTree=template};
            }
            button.Click+=(_,_)=>EditControllerInput(key);controllerButtons[key]=button;
            System.Windows.Automation.AutomationProperties.SetName(button,row.DisplayInput);
            if(key is "Select" or "Start"||key.StartsWith("DPad")||key is "L3" or "R3")continue;
            double ly=pos.key switch {"ZL"=>100,"L"=>172,"ZR"=>100,"R"=>172,"X"=>239,"Y"=>299,"A"=>359,"B"=>419,_=>0};
            bool left=pos.x<500;
            AddCallout(row,left?14:850,ly,left?pos.x+4:pos.x+pos.w-4,pos.y+pos.h/2,left);
        }
        AddGroupCallout("十字键","四向快捷键","DPadUp",14,317,305,340,true);
        AddGroupCallout("左摇杆","四向快捷键","L3",14,485,345,485,true);
        AddGroupCallout("右摇杆","四向快捷键","R3",850,485,675,485,false);
        UpdateControllerHighlights(new HashSet<string>());
    }
    string ShortAction(BindingRow row)=>row.Action switch {"UndoRepeat"=>"连续撤销","RedoRepeat"=>"连续重做","Smudge" or "SmudgeLegacy"=>"按住涂抹","TemporaryEraser"=>"临时橡皮擦","None"=>"未设置",_=>row.ActionLabel};
    void AddGroupCallout(string title,string subtitle,string input,double x,double y,double ax,double ay,bool left)
    {
        var row=rows.FirstOrDefault(r=>r.Input==input);if(row is null)return;
        AddCallout(row,x,y,ax,ay,left,title,subtitle);
    }
    void AddCallout(BindingRow row,double x,double y,double ax,double ay,bool left,string? title=null,string? subtitle=null)
    {
        double edge=left?194:850,elbow=left?215:825;
        var line=new Polyline {Stroke=Ink("#8A939C"),StrokeThickness=1.2,IsHitTestVisible=false};
        line.Points=new PointCollection {new System.Windows.Point(ax,ay),new System.Windows.Point(elbow,y+19),new System.Windows.Point(edge,y+19)};
        ControllerCanvas.Children.Add(line);
        var dot=new Ellipse{Width=7,Height=7,Fill=Ink("#8A939C"),Stroke=Ink("#FAFAF8"),StrokeThickness=1.4,IsHitTestVisible=false};Canvas.SetLeft(dot,ax-3.5);Canvas.SetTop(dot,ay-3.5);ControllerCanvas.Children.Add(dot);
        var panel=new StackPanel();
        var label=new TextBlock {Text=title??BindingRow.InputLabel(row.Input)+"  "+ShortAction(row),FontSize=21,TextWrapping=TextWrapping.Wrap};
        panel.Children.Add(label);
        if(subtitle!=null)panel.Children.Add(new TextBlock {Text=subtitle,FontSize=18,Foreground=Ink("#6A7076"),Margin=new Thickness(0,4,0,0)});
        else if(selectedInput==row.Input&&rows.Any(r=>r.Input==row.Input+".Double"&&r.Action!="None"))
        {var doubleRow=rows.First(r=>r.Input==row.Input+".Double");panel.Children.Add(new TextBlock{Text="双击 · "+ShortAction(doubleRow),FontSize=14,Foreground=Ink("#6A7076"),Margin=new Thickness(0,4,0,0),TextWrapping=TextWrapping.Wrap});}
        if(selectedInput==row.Input&&rows.FirstOrDefault(r=>r.Input==row.Input+".Hold") is BindingRow held && held.Action!="None")
            panel.Children.Add(new TextBlock{Text="长按 · "+ShortAction(held),FontSize=14,Foreground=Ink("#6A7076"),TextWrapping=TextWrapping.Wrap});
        var button=new Button{Content=panel,Width=184,Padding=new Thickness(7,6,5,6),Background=Brushes.Transparent,Foreground=Ink("#353A3D"),HorizontalContentAlignment=System.Windows.HorizontalAlignment.Left};
        Canvas.SetLeft(button,x);Canvas.SetTop(button,y);ControllerCanvas.Children.Add(button);
        button.Click+=(_,_)=>EditControllerInput(row.Input);callouts[row.Input]=button;
        button.ToolTip=row.ActionLabel;
    }
    void UpdateControllerHighlights(IReadOnlySet<string> pressed,bool disconnected=false)
    {
        long now=Environment.TickCount64;
        if(disconnected)inputLastSeen.Clear();
        foreach(var key in pressed)inputLastSeen[key]=now;
        var visible=inputLastSeen.Where(p=>now-p.Value<140).Select(p=>p.Key).ToHashSet();
        highlightedInputs.Clear();highlightedInputs.UnionWith(visible);
        bool IsActive(string key) => visible.Contains(key)||selectedInput==key||
            (key=="L3"&&(visible.Any(k=>k.StartsWith("LeftStick"))||selectedInput?.StartsWith("LeftStick")==true))||
            (key=="R3"&&(visible.Any(k=>k.StartsWith("RightStick"))||selectedInput?.StartsWith("RightStick")==true));
        foreach(var pair in controllerButtons)
        {
            bool active=IsActive(pair.Key);
            pair.Value.Background=active?Ink("#5278A0"):pair.Key is "L3" or "R3"?Ink("#BFC0BD"):pair.Key.StartsWith("DPad")||pair.Key is "L" or "R" or "ZL" or "ZR"?Brushes.Transparent:Ink("#F0EFEC");
            pair.Value.Foreground=active?Brushes.White:Ink("#353A3D");
            System.Windows.Automation.AutomationProperties.SetItemStatus(pair.Value,visible.Contains(pair.Key)?"按下":"松开");
        }
        foreach(var pair in callouts)
        {
            bool active=IsActive(pair.Key)||(pair.Key=="DPadUp"&&visible.Any(k=>k.StartsWith("DPad")));
            pair.Value.Background=active?Ink("#E3EBF2"):Brushes.Transparent;
            pair.Value.Foreground=active?Ink("#426B93"):Ink("#353A3D");
        }
    }

    void EditControllerInput(string input)
    {
        if(currentProfile is null||!ResolveUnsaved())return;
        engine.Enabled=false;engine.Reset();ToggleButton.Content="启动映射";
        selectedInput=input;InspectorPanel.Children.Clear();InspectorActions.Children.Clear();
        var heading=new DockPanel{Margin=new Thickness(0,14,0,24)};
        var close=new Button{Content="×",Background=Brushes.Transparent,Foreground=Ink("#72777B"),FontSize=24,Padding=new Thickness(8,0,8,0)};
        DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);
        var badge=new Border{Background=Ink("#5278A0"),CornerRadius=new CornerRadius(22),Width=44,Height=44,Margin=new Thickness(0,0,12,0),Child=new TextBlock {Text=BindingRow.InputLabel(input).Length>3?"↔":BindingRow.InputLabel(input),Foreground=Brushes.White,FontSize=20,HorizontalAlignment=System.Windows.HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        DockPanel.SetDock(badge,Dock.Left);heading.Children.Add(badge);
        heading.Children.Add(new TextBlock{Text=BindingRow.InputLabel(input)+(input.Length==1?" 键":""),FontSize=23,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center});
        InspectorPanel.Children.Add(heading);
        close.Click+=(_,_)=>{if(!ResolveInspectorDraft())return;InspectorPanel.Children.Clear();InspectorActions.Children.Clear();selectedInput=null;inspectorDraft=null;BuildControllerDiagram();};
        if(input.StartsWith("DPad")||input.Contains("Stick")||input is "L3" or "R3")
        {
            string prefix=input.StartsWith("DPad")?"DPad":input=="L3"||input.StartsWith("Left")?"LeftStick":"RightStick";
            var choices=new ComboBox {ItemsSource=rows.Where(r=>!r.Input.EndsWith(".Hold")&&!r.Input.EndsWith(".Double")&&(r.Input.StartsWith(prefix)||r.Input==(prefix=="LeftStick"?"L3":prefix=="RightStick"?"R3":"DPadUp"))).ToList(),DisplayMemberPath="DisplayInput",SelectedValuePath="Input",SelectedValue=input,Margin=new Thickness(0,0,0,18)};
            choices.SelectionChanged+=(_,_)=>{if(choices.SelectedValue is string key&&key!=input)EditControllerInput(key);};
            InspectorPanel.Children.Add(choices);
        }
        var edited=rows.Where(r=>r.Input==input||r.Input==input+".Double"||r.Input==input+".Hold").Select(r=>new BindingRow {Input=r.Input,Action=r.Action,Shortcut=r.Shortcut}).ToList();
        inspectorDraft=edited;
        var tabs=new UniformGrid {Columns=edited.Count,Margin=new Thickness(0,0,0,20)};
        InspectorPanel.Children.Add(tabs);
        var body=new StackPanel();InspectorPanel.Children.Add(body);
        int selectedTab=0;
        var tabButtons=new List<Button>();
        void Divider()=>body.Children.Add(new Border{Height=1,Background=Ink("#DADDDD"),Margin=new Thickness(0,22,0,22)});
        void RenderEditor()
        {
            body.Children.Clear();
            for(int i=0;i<tabButtons.Count;i++){tabButtons[i].Background=selectedTab==i?Ink("#E1E9F0"):Brushes.Transparent;tabButtons[i].Foreground=Ink("#353A3D");}
            var row=edited[selectedTab];
            bool holdTab=row.Input.EndsWith(".Hold");
            body.Children.Add(new TextBlock{Text=holdTab?"长按功能":"功能",FontSize=19,Margin=new Thickness(0,0,0,12)});
            var actions=new ComboBox{ItemsSource=ActionOptions,DisplayMemberPath="Value",SelectedValuePath="Key",SelectedValue=row.Action};
            body.Children.Add(actions);
            var recorder=new TextBox{DataContext=row,IsReadOnly=true,Margin=new Thickness(0,12,0,0),ToolTip="点击后按键盘录制"};
            recorder.SetBinding(TextBox.TextProperty,new System.Windows.Data.Binding("Shortcut"){Mode=System.Windows.Data.BindingMode.TwoWay});
            System.Windows.Input.InputMethod.SetIsInputMethodEnabled(recorder,false);
            recorder.GotKeyboardFocus+=ShortcutRecorder_GotKeyboardFocus;recorder.PreviewKeyDown+=ShortcutRecorder_PreviewKeyDown;
            recorder.Visibility=row.Action=="CustomKey"?Visibility.Visible:Visibility.Collapsed;body.Children.Add(recorder);
            actions.SelectionChanged+=(_,_)=>{row.Action=actions.SelectedValue as string??"None";recorder.Visibility=row.Action=="CustomKey"?Visibility.Visible:Visibility.Collapsed;};
            if(holdTab)body.Children.Add(new TextBlock{Text=row.Action is "UndoRepeat" or "RedoRepeat"
                ? "与单击的撤销/重做配对时：按下执行一次，按住按连发设置重复。"
                : "持续按住 400 毫秒触发，松开结束；不会触发单击。",FontSize=12,Foreground=Ink("#71777D"),Margin=new Thickness(0,10,0,0),TextWrapping=TextWrapping.Wrap});
            Divider();
            body.Children.Add(new TextBlock{Text="常用功能",FontSize=19,Margin=new Thickness(0,0,0,14)});
            var quick=new UniformGrid{Columns=2};
            foreach(var pair in new[]{("Brush","画笔"),("Eraser","橡皮擦"),("Undo","撤销"),("Redo","重做"),("Smudge","涂抹"),("Layers","图层")})
            {
                var option=new Button{Content=pair.Item2,Background=row.Action==pair.Item1?Ink("#E1E9F0"):Brushes.Transparent,Foreground=Ink("#353A3D"),BorderBrush=Ink("#D1D5D7"),BorderThickness=new Thickness(1),Margin=new Thickness(0,0,8,10),Padding=new Thickness(8,12,8,12),FontSize=18};
                option.Click+=(_,_)=>{row.Action=pair.Item1;RenderEditor();};quick.Children.Add(option);
            }
            body.Children.Add(quick);Divider();
            var recordLine=new DockPanel();
            var record=new Button{Content="○  录制",Background=Brushes.Transparent,Foreground=Ink("#353A3D"),BorderBrush=Ink("#C3C9CD"),BorderThickness=new Thickness(1)};
            DockPanel.SetDock(record,Dock.Right);recordLine.Children.Add(record);
            recordLine.Children.Add(new TextBlock{Text="自定义快捷键",FontSize=19,VerticalAlignment=VerticalAlignment.Center});body.Children.Add(recordLine);
            record.Click+=(_,_)=>{row.Action="CustomKey";actions.SelectedValue="CustomKey";recorder.Visibility=Visibility.Visible;recorder.Focus();};
        }
        var headers=edited.Select(r=>r.Input.EndsWith(".Hold")?"长按":r.Input.EndsWith(".Double")?"双击":"单击").ToArray();
        for(int i=0;i<headers.Length;i++)
        {
            int index=i;var tab=new Button{Content=headers[i],Background=Brushes.Transparent,Foreground=Ink("#353A3D"),Padding=new Thickness(6,11,6,11),FontSize=18};
            tab.Click+=(_,_)=>{selectedTab=index;RenderEditor();};tabButtons.Add(tab);tabs.Children.Add(tab);
        }
        RenderEditor();
        var buttons=new UniformGrid{Columns=2,Margin=new Thickness(0,30,0,0)};
        var save=new Button {Content="保存",Margin=new Thickness(0,0,8,0),FontSize=18};
        var cancel=new Button{Content="取消",Background=Brushes.Transparent,Foreground=Ink("#353A3D"),BorderBrush=Ink("#C3C9CD"),BorderThickness=new Thickness(1),FontSize=18};
        buttons.Children.Add(save);buttons.Children.Add(cancel);InspectorActions.Children.Add(buttons);
        save.Click+=(_,_)=>{
            if(edited.Any(r=>r.Input.EndsWith(".Double")&&r.Action is "Smudge" or "SmudgeLegacy" or "TemporaryEraser")){ShowToast("按住工具请设置在单击或长按页");return;}
            if(edited.Any(r=>r.Action=="CustomKey"&&!KeyboardOutput.IsValidShortcut(r.Shortcut))){ShowToast("请先录制有效快捷键");return;}
            if(edited.Any(r=>r.Input==input && r.Action is "Smudge" or "SmudgeLegacy" or "TemporaryEraser") &&
                edited.Any(r=>r.Input==input+".Hold"&&r.Action!="None"))
            {ShowToast("单击请选择普通动作；按住工具请设置在长按页");return;}
            foreach(var edit in edited){var original=rows.First(r=>r.Input==edit.Input);original.Action=edit.Action;original.Shortcut=edit.Shortcut;}
            SaveProfile_Click(this,new RoutedEventArgs());
            if(!MappingDirty()){BuildControllerDiagram();ShowToast("已保存按键映射 · 映射已暂停");}
        };
        cancel.Click+=(_,_)=>{InspectorPanel.Children.Clear();InspectorActions.Children.Clear();selectedInput=null;inspectorDraft=null;BuildControllerDiagram();ShowToast("已取消编辑");};
        var note=new TextBlock {Text="编辑期间映射暂停",FontSize=12,Foreground=Ink("#767D83"),HorizontalAlignment=System.Windows.HorizontalAlignment.Center,Margin=new Thickness(0,20,0,0)};
        InspectorActions.Children.Add(note);
        BuildControllerDiagram();
    }
}
