import subprocess
import json

def run_adb(cmd):
    result = subprocess.run(f'c:/Games/RMX3171 ADB tool/adb.exe shell {cmd}', capture_output=True, text=True, shell=True)
    return result.stdout.strip()

# get all packages
all_pkgs_output = run_adb("pm list packages")
all_pkgs = []
for line in all_pkgs_output.split('\n'):
    line = line.strip().replace("package:", "")
    if line.startswith("com.coloros.") or line.startswith("com.heytap.") or line.startswith("com.oplus.") or line.startswith("com.oppo."):
        all_pkgs.append(line)

# Let's write a C# script to invoke PackageRiskEvaluator to see how it currently evaluates these packages
