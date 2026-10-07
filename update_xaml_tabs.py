import sys
import re

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\MemoryManagerView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

# Create the new TabItem
new_tab = '''            <!-- TAB 4 - DISABLED APPS -->
            <TabItem Header="{Binding DisabledCountLabel}">
                <Border Style="{StaticResource CardStyle}" Margin="0,8,0,0">
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="*"/>
                        </Grid.RowDefinitions>

                        <Border Grid.Row="0" Background="#1A2A3A" BorderBrush="#204060" BorderThickness="1"
                                CornerRadius="4" Padding="10" Margin="0,0,0,10">
                            <TextBlock TextWrapping="Wrap" Foreground="#A0C0E0" FontSize="12">
                                <Run FontWeight="Bold">[i] DISABLED APPS.</Run>
                                <Run>These packages are currently disabled on the phone and do not consume RAM. Use the Enable button to restore them.</Run>
                            </TextBlock>
                        </Border>

                        <DataGrid Grid.Row="1" ItemsSource="{Binding DisabledAppProcesses}" AutoGenerateColumns="False"
                                  CanUserAddRows="False" IsReadOnly="False" HeadersVisibility="Column"
                                  Background="Transparent" BorderThickness="0"
                                  RowBackground="Transparent" AlternatingRowBackground="#262629"
                                  GridLinesVisibility="None" Foreground="White" CanUserSortColumns="True">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Package Name" Binding="{Binding PackageName}" Width="*" IsReadOnly="True"/>
                                <DataGridTextColumn Header="State" Binding="{Binding StateText}" Width="150" IsReadOnly="True" FontWeight="Bold"/>
                                <DataGridTextColumn Header="Recommendation" Binding="{Binding Recommendation}" Width="200" IsReadOnly="True"/>
                                <DataGridTemplateColumn Header="Action" Width="100">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate>
                                            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                                                <Button Content="Enable" Command="{Binding DataContext.EnablePackageCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" 
                                                        CommandParameter="{Binding PackageName}"
                                                        Background="#2E7D32" Foreground="White" BorderThickness="0" Padding="10,4" Margin="0,0,5,0"
                                                        Visibility="{Binding IsDisabled, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                                            </StackPanel>
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>
                    </Grid>
                </Border>
            </TabItem>
        </TabControl>'''

# Inject the new tab just before </TabControl>
content = content.replace('        </TabControl>', new_tab)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
