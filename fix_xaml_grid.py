import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\Views\Storage\CleanupOpportunitiesView.xaml'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

import re

# find the DataGrid block and fix it
# Instead of searching the whole datagrid, let's just replace the whole Columns element.
pattern = r'<DataGrid.Columns>.*?</DataGrid>'

replacement = '''<DataGrid.Columns>
                    <DataGridTemplateColumn Width="40">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <CheckBox IsChecked="{Binding IsSelected, UpdateSourceTrigger=PropertyChanged}" HorizontalAlignment="Center" VerticalAlignment="Center"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                    
                    <DataGridTextColumn Header="Category" Binding="{Binding Category}" Width="120">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="#FFAAAAAA"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>
                    
                    <DataGridTextColumn Header="Name" Binding="{Binding FileName}" Width="250">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="White"/>
                                <Setter Property="FontWeight" Value="SemiBold"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>
                    
                    <DataGridTextColumn Header="Size" Binding="{Binding SizeText}" Width="80">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="White"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>
                    
                    <DataGridTextColumn Header="Modified" Binding="{Binding DateModifiedText}" Width="130">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="#FFAAAAAA"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>
                    
                    <DataGridTextColumn Header="Risk Level" Binding="{Binding RecommendationLevel}" Width="150">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="{StaticResource PrimaryBrush}"/>
                                <Setter Property="FontWeight" Value="SemiBold"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTextColumn Header="Reason" Binding="{Binding Reason}" Width="*">
                        <DataGridTextColumn.ElementStyle>
                            <Style TargetType="TextBlock">
                                <Setter Property="Foreground" Value="#FFFFD700"/>
                            </Style>
                        </DataGridTextColumn.ElementStyle>
                    </DataGridTextColumn>

                    <DataGridTemplateColumn Width="100">
                        <DataGridTemplateColumn.CellTemplate>
                            <DataTemplate>
                                <Button Content="Preview" 
                                        Command="{Binding DataContext.PreviewCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}" 
                                        CommandParameter="{Binding}"
                                        Background="#FF007ACC" Foreground="White" BorderThickness="0" Padding="10,4" Margin="5" Cursor="Hand"/>
                            </DataTemplate>
                        </DataGridTemplateColumn.CellTemplate>
                    </DataGridTemplateColumn>
                </DataGrid.Columns>
            </DataGrid>'''

content = re.sub(pattern, replacement, content, flags=re.DOTALL)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
