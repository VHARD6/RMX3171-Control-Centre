import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/>\n                <RadioButton Content="Cache Cleaner" Command="{Binding NavigateCommand}" CommandParameter="Cache"\n                             Style="{StaticResource NavButtonStyle}" Margin="0,0,0,10"/> Margin="10,5" HorizontalAlignment="Stretch"/>', 'Margin="10,5" HorizontalAlignment="Stretch"/>\n                <Button Content="Cache Cleaner" Command="{Binding NavigateCommand}" CommandParameter="Cache" Margin="10,5" HorizontalAlignment="Stretch"/>')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
