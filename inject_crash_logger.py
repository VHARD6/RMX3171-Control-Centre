import sys

path = r'C:\Users\omras\RMX3171ControlCentre_V2\App.xaml.cs'
with open(path, 'r', encoding='utf-8') as f:
    content = f.read()

injection = '''
        protected override void OnStartup(StartupEventArgs e)
        {
            this.DispatcherUnhandledException += (s, args) => 
            {
                System.IO.File.WriteAllText("crash.txt", args.Exception.ToString());
            };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => 
            {
                System.IO.File.WriteAllText("crash2.txt", args.ExceptionObject.ToString());
            };
            base.OnStartup(e);
        }
'''

# Find public partial class App : Application
content = content.replace('public partial class App : Application\r\n    {', 'public partial class App : Application\r\n    {\r\n' + injection)

with open(path, 'w', encoding='utf-8') as f:
    f.write(content)
