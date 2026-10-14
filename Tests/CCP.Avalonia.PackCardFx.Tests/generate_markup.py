from pathlib import Path
import re,sys
source=Path(sys.argv[1] if len(sys.argv)>1 else Path(__file__).resolve().parents[2]/'CCP.Avalonia/Views/Tabs/AssetsTabView.axaml').read_text()
tag=re.search(r'<Border Classes="packCard".*?BorderThickness="1">',source,re.S).group(0)
markup='''<UserControl xmlns="https://github.com/avaloniaui" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" x:Class="ConditioningControlPanel.Avalonia.Views.Tabs.AssetsTabView">
<UserControl.Resources><SolidColorBrush x:Key="SurfaceBgBrush" Color="#303050"/><SolidColorBrush x:Key="PanelAccentBrush" Color="#FF69B4"/></UserControl.Resources>
<StackPanel Orientation="Horizontal">'''
for i in (1,2):markup+=tag.replace('<Border Classes=',f'<Border x:Name="Card{i}" Classes=')+f'<TextBlock Text="Pack {i}" Foreground="White" Margin="12"/></Border>'
markup+='</StackPanel></UserControl>'
Path(__file__).with_name('AssetsTabView.axaml').write_text(markup)
