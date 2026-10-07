import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MemoryManagerView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace all Action columns
# They currently look like:
# <DataGridTemplateColumn Header="Action" Width="110" CanUserSort="False"> ... </DataGridTemplateColumn>
# And RAM column:
# <DataGridTextColumn Header="RAM - PSS" ... /> or <DataGridTextColumn Header="RAM" ... />

# We also need to add State column next to RAM.
ram_pattern = r'(<DataGridTextColumn Header="RAM[^"]*"[^>]*>)'
ram_replace = r'\1\n                                <DataGridTextColumn Header="State" Binding="{Binding StateText}" Width="80" IsReadOnly="True"/>'
content = re.sub(ram_pattern, ram_replace, content)

action_pattern = r'<DataGridTemplateColumn Header="Action"[^>]*>.*?</DataGridTemplateColumn>'
new_action = '''<DataGridTemplateColumn Header="Action" Width="200" CanUserSort="False">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                                <Button Content="Force Stop" 
                                                        Command="{Binding DataContext.ForceStopCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" 
                                                        CommandParameter="{Binding}"
                                                        Background="#444448" Foreground="White" Padding="8,3" Margin="2" BorderThickness="0" Cursor="Hand"
                                                        Visibility="{Binding IsDisabled, Converter={StaticResource InverseBooleanToVisibilityConverter}}">
                                                    <Button.Style>
                                                        <Style TargetType="Button">
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding CanForceStop}" Value="False">
                                                                    <Setter Property="Content" Value="Protected"/>
                                                                    <Setter Property="Background" Value="Transparent"/>
                                                                    <Setter Property="Foreground" Value="#666666"/>
                                                                    <Setter Property="IsEnabled" Value="False"/>
                                                                    <Setter Property="Cursor" Value="Arrow"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Button.Style>
                                                </Button>
                                                <Button Content="Disable" 
                                                        Command="{Binding DataContext.DisablePackageCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" 
                                                        CommandParameter="{Binding}"
                                                        Background="#883333" Foreground="White" Padding="8,3" Margin="2" BorderThickness="0" Cursor="Hand"
                                                        Visibility="{Binding IsDisabled, Converter={StaticResource InverseBooleanToVisibilityConverter}}">
                                                    <Button.Style>
                                                        <Style TargetType="Button">
                                                            <Style.Triggers>
                                                                <DataTrigger Binding="{Binding CanForceStop}" Value="False">
                                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                                </DataTrigger>
                                                            </Style.Triggers>
                                                        </Style>
                                                    </Button.Style>
                                                </Button>
                                                <Button Content="Enable" 
                                                        Command="{Binding DataContext.EnablePackageCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" 
                                                        CommandParameter="{Binding}"
                                                        Background="#338833" Foreground="White" Padding="8,3" Margin="2" BorderThickness="0" Cursor="Hand"
                                                        Visibility="{Binding IsDisabled, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>'''
content = re.sub(action_pattern, new_action, content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
