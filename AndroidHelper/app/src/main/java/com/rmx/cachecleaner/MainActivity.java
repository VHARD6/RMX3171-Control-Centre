package com.rmx.cachecleaner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.os.Bundle;

public class MainActivity extends Activity {
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        
        new AlertDialog.Builder(this)
            .setTitle("CACHE CLEANER")
            .setMessage("Android 11 restricts third-party apps from globally clearing other apps' caches.\\n\\nTo clear your cache, we will open the System Storage settings where you can safely clear cached data.")
            .setPositiveButton("OPEN SETTINGS", (dialog, which) -> {
                Intent intent = new Intent(android.provider.Settings.ACTION_INTERNAL_STORAGE_SETTINGS);
                startActivity(intent);
                finish();
            })
            .setNegativeButton("CANCEL", (dialog, which) -> finish())
            .setOnCancelListener(dialog -> finish())
            .show();
    }
}
