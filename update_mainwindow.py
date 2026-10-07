import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

nav_item = '''
                <RadioButton Content="Cache Cleaner" Command="{Binding NavigateCommand}" CommandParameter="Cache"
                             Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/>'''

if 'CommandParameter="Cache"' not in content:
    content = content.replace('CommandParameter="Memory"', 'CommandParameter="Memory" Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/>' + nav_item)
    # clean up the replaced string just in case it caused duplicates
    content = content.replace('Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/> Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/>', 'Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/>')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
