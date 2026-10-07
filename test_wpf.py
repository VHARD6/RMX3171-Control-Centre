import clr
clr.AddReference('PresentationFramework')
from System.Windows.Controls import DataGridTextColumn
try:
    prop = DataGridTextColumn().FontWeight
    print('Has property')
except Exception as e:
    print('Error:', e)
