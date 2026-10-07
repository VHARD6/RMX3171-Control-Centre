import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MainWindow.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

template = '''        <DataTemplate DataType="{x:Type viewmodels:CacheManagerViewModel}">
            <views:CacheManagerView />
        </DataTemplate>
'''

if 'CacheManagerViewModel' not in content:
    content = content.replace('        <DataTemplate DataType="{x:Type viewmodels:AppManagerViewModel}">', template + '        <DataTemplate DataType="{x:Type viewmodels:AppManagerViewModel}">')
    with open(path, 'w', encoding='utf-8') as f:
        f.write(content)
