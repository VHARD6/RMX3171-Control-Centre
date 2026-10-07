import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MemoryManagerView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

content = content.replace('<DataGridTextColumn Header="State" Binding="{Binding StateText}" Width="150" IsReadOnly="True" FontWeight="Bold"/>', '<DataGridTextColumn Header="State" Binding="{Binding StateText}" Width="150" IsReadOnly="True"/>')

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
