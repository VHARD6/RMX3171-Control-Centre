package com.rmx.cachecleaner;

import android.app.Activity;
import android.os.Bundle;
import android.view.View;
import android.widget.Button;
import android.widget.TextView;

/**
 * Phase 4.2: Step 2 Final Confirmation Dialog Activity
 * 
 * Presents the observed cache values and safety confirmations to the user
 * before the Accessibility Service executes the final "Clear cache" click.
 */
public class PilotConfirmationDialogActivity extends Activity {

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setFinishOnTouchOutside(false);
        setContentView(R.layout.dialog_pilot_confirmation);

        String targetLabel = getIntent().getStringExtra("target_label");
        String pkg = getIntent().getStringExtra("target_package");
        if (pkg == null) pkg = PilotController.TARGET_PACKAGE;
        if (targetLabel == null || targetLabel.isEmpty()) targetLabel = "Instagram";

        String cacheBefore = getIntent().getStringExtra("cache_before");
        if (cacheBefore == null || cacheBefore.isEmpty()) cacheBefore = "Unknown";

        TextView tvPkg = findViewById(R.id.tv_dialog_pkg);
        TextView tvCache = findViewById(R.id.tv_dialog_cache_size);
        Button btnCancel = findViewById(R.id.btn_dialog_cancel);
        Button btnProceed = findViewById(R.id.btn_dialog_proceed);

        if (tvPkg != null) {
            tvPkg.setText("Target: " + targetLabel + " (" + pkg + ")");
        }

        if (tvCache != null) {
            tvCache.setText("Observed Cache: " + cacheBefore);
        }

        btnCancel.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                PilotController.getInstance().cancel("User cancelled at Step 2 confirmation dialog.");
                finish();
            }
        });

        btnProceed.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                PilotController.getInstance().confirmFinalExecution();
                finish();
            }
        });
    }

    @Override
    public void onBackPressed() {
        PilotController.getInstance().cancel("User pressed Back at Step 2 confirmation dialog.");
        super.onBackPressed();
    }
}
