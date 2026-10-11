package com.rmx.cachecleaner;

import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.graphics.drawable.Drawable;
import android.os.Bundle;
import android.provider.Settings;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;
import android.app.Activity;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;

/**
 * Phase 5: Multi-App Selection and Batch Cleaning Activity.
 * Displays allowlisted third-party applications, handles user selection,
 * displays upfront batch confirmation, and monitors guarded sequential execution.
 */
public class AppSelectionActivity extends Activity implements PilotController.BatchListener {

    private LinearLayout llTargetAppsList;
    private TextView tvSelectionCount;
    private Button btnToggleSelectAll;
    private Button btnCleanSelected;
    private Button btnCancelBatch;
    private TextView tvEmptyNotice;

    // Batch progress & summary containers
    private LinearLayout llBatchProgress;
    private TextView tvBatchProgressMessage;
    private ProgressBar pbBatchProgress;
    private LinearLayout llBatchSummary;
    private TextView tvBatchSummaryText;

    private List<TargetApp> targetApps = new ArrayList<>();
    private final Map<String, View> appRowViews = new HashMap<>();
    private boolean isAllSelected = true;
    private boolean isCleaningInProgress = false;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_app_selection);

        initViews();
        loadSupportedApps();
    }

    private void initViews() {
        ImageButton btnBack = findViewById(R.id.btn_back);
        if (btnBack != null) {
            btnBack.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (isCleaningInProgress) {
                        Toast.makeText(AppSelectionActivity.this, "Batch cleaning is currently running.", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    finish();
                }
            });
        }

        llTargetAppsList = findViewById(R.id.ll_target_apps_list);
        tvSelectionCount = findViewById(R.id.tv_selection_count);
        btnToggleSelectAll = findViewById(R.id.btn_toggle_select_all);
        btnCleanSelected = findViewById(R.id.btn_clean_selected);
        btnCancelBatch = findViewById(R.id.btn_cancel_batch);
        tvEmptyNotice = findViewById(R.id.tv_empty_notice);

        llBatchProgress = findViewById(R.id.ll_batch_progress);
        tvBatchProgressMessage = findViewById(R.id.tv_batch_progress_message);
        pbBatchProgress = findViewById(R.id.pb_batch_progress);
        llBatchSummary = findViewById(R.id.ll_batch_summary);
        tvBatchSummaryText = findViewById(R.id.tv_batch_summary_text);

        btnToggleSelectAll.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                toggleSelectAll();
            }
        });

        btnCleanSelected.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showBatchConfirmationDialog();
            }
        });

        btnCancelBatch.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                PilotController.getInstance().cancelBatch("User cancelled via UI button");
            }
        });
    }

    private void loadSupportedApps() {
        PackageManager pm = getPackageManager();
        targetApps = SupportedAppsRegistry.getInstalledSupportedApps(pm);

        llTargetAppsList.removeAllViews();
        appRowViews.clear();

        if (targetApps.isEmpty()) {
            tvEmptyNotice.setVisibility(View.VISIBLE);
            btnCleanSelected.setEnabled(false);
            btnToggleSelectAll.setEnabled(false);
            tvSelectionCount.setText("Supported Applications (0)");
            return;
        }

        tvEmptyNotice.setVisibility(View.GONE);
        btnCleanSelected.setEnabled(true);
        btnToggleSelectAll.setEnabled(true);

        LayoutInflater inflater = LayoutInflater.from(this);
        for (final TargetApp app : targetApps) {
            View row = inflater.inflate(R.layout.item_target_app, llTargetAppsList, false);

            final CheckBox cbSelect = row.findViewById(R.id.cb_app_select);
            ImageView ivIcon = row.findViewById(R.id.iv_app_icon);
            TextView tvLabel = row.findViewById(R.id.tv_app_label);
            TextView tvPackage = row.findViewById(R.id.tv_app_package);
            TextView tvDetail = row.findViewById(R.id.tv_app_detail);
            TextView tvStatusBadge = row.findViewById(R.id.tv_app_status_badge);

            tvLabel.setText(app.getResolvedLabel());
            tvPackage.setText(app.getPackageName());
            tvDetail.setText(app.getStatusDetail());
            cbSelect.setChecked(app.isSelected());

            // Load app icon
            try {
                Drawable icon = pm.getApplicationIcon(app.getPackageName());
                ivIcon.setImageDrawable(icon);
            } catch (Exception ignored) {}

            updateBadgeAppearance(tvStatusBadge, app.getStatus());

            cbSelect.setOnCheckedChangeListener(new CompoundButton.OnCheckedChangeListener() {
                @Override
                public void onCheckedChanged(CompoundButton buttonView, boolean isChecked) {
                    app.setSelected(isChecked);
                    updateSelectionSummary();
                }
            });

            row.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (!isCleaningInProgress) {
                        cbSelect.setChecked(!cbSelect.isChecked());
                    }
                }
            });

            appRowViews.put(app.getPackageName(), row);
            llTargetAppsList.addView(row);
        }

        updateSelectionSummary();
    }

    private void updateSelectionSummary() {
        int selected = 0;
        for (TargetApp app : targetApps) {
            if (app.isSelected()) selected++;
        }
        tvSelectionCount.setText(String.format(Locale.US, "Supported Applications (%d of %d selected)", selected, targetApps.size()));
        btnCleanSelected.setText(String.format(Locale.US, "CLEAN SELECTED APPS (%d)", selected));
        btnCleanSelected.setEnabled(selected > 0 && !isCleaningInProgress);
        isAllSelected = (selected == targetApps.size());
        btnToggleSelectAll.setText(isAllSelected ? "Deselect All" : "Select All");
    }

    private void toggleSelectAll() {
        boolean newState = !isAllSelected;
        for (TargetApp app : targetApps) {
            app.setSelected(newState);
            View row = appRowViews.get(app.getPackageName());
            if (row != null) {
                CheckBox cb = row.findViewById(R.id.cb_app_select);
                if (cb != null) cb.setChecked(newState);
            }
        }
        updateSelectionSummary();
    }

    private void showBatchConfirmationDialog() {
        final List<TargetApp> selectedApps = new ArrayList<>();
        for (TargetApp app : targetApps) {
            if (app.isSelected()) selectedApps.add(app);
        }

        if (selectedApps.isEmpty()) {
            Toast.makeText(this, "Please select at least one application to clean.", Toast.LENGTH_SHORT).show();
            return;
        }

        if (!SettingsInspectionAccessibilityService.isServiceRunning()) {
            new AlertDialog.Builder(this)
                    .setTitle("Accessibility Service Required")
                    .setMessage("Multi-app cache cleaning requires RMX Cache Cleaner's Accessibility Probe to be active in Android Settings.\n\nPlease enable it and try again.")
                    .setPositiveButton("Open Settings", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            try {
                                startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
                            } catch (Exception e) {
                                Toast.makeText(AppSelectionActivity.this, "Unable to open Settings.", Toast.LENGTH_SHORT).show();
                            }
                        }
                    })
                    .setNegativeButton(android.R.string.cancel, null)
                    .show();
            return;
        }

        StringBuilder msg = new StringBuilder();
        msg.append("You are about to clear the private cache for the following ");
        msg.append(selectedApps.size()).append(" application(s):\n\n");
        for (TargetApp app : selectedApps) {
            msg.append("• ").append(app.getResolvedLabel())
               .append(" (").append(app.getPackageName()).append(")\n");
        }
        msg.append("\nEach application will be opened sequentially, identity verified on App Info and Storage usage, and only its cache cleared.\n\n");
        msg.append("Personal account data and app storage will NOT be affected.\n\nProceed?");

        new AlertDialog.Builder(this)
                .setTitle("Confirm Multi-App Cleaning")
                .setMessage(msg.toString())
                .setPositiveButton("START CLEANING", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        startBatchExecution(selectedApps);
                    }
                })
                .setNegativeButton("CANCEL", null)
                .show();
    }

    private void startBatchExecution(List<TargetApp> selectedApps) {
        isCleaningInProgress = true;
        btnCleanSelected.setEnabled(false);
        btnToggleSelectAll.setEnabled(false);
        btnCancelBatch.setVisibility(View.VISIBLE);

        llBatchSummary.setVisibility(View.GONE);
        llBatchProgress.setVisibility(View.VISIBLE);
        tvBatchProgressMessage.setText(String.format(Locale.US, "Starting batch of %d app(s)...", selectedApps.size()));

        // Reset display state of rows
        for (TargetApp app : targetApps) {
            if (app.isSelected()) {
                app.setStatus(TargetApp.Status.PENDING);
                app.setStatusDetail("Pending");
            } else {
                app.setStatus(TargetApp.Status.SKIPPED);
                app.setStatusDetail("Not selected");
            }
            updateAppRowDisplay(app);
        }

        boolean started = PilotController.getInstance().startBatch(this, selectedApps, this);
        if (!started) {
            isCleaningInProgress = false;
            btnCleanSelected.setEnabled(true);
            btnToggleSelectAll.setEnabled(true);
            btnCancelBatch.setVisibility(View.GONE);
            llBatchProgress.setVisibility(View.GONE);
            Toast.makeText(this, "Failed to start batch cleaner.", Toast.LENGTH_SHORT).show();
        }
    }

    private void updateAppRowDisplay(TargetApp app) {
        View row = appRowViews.get(app.getPackageName());
        if (row != null) {
            TextView tvDetail = row.findViewById(R.id.tv_app_detail);
            TextView tvBadge = row.findViewById(R.id.tv_app_status_badge);
            if (tvDetail != null) tvDetail.setText(app.getStatusDetail());
            if (tvBadge != null) updateBadgeAppearance(tvBadge, app.getStatus());
        }
    }

    private void updateBadgeAppearance(TextView badge, TargetApp.Status status) {
        if (badge == null || status == null) return;
        switch (status) {
            case PENDING:
                badge.setText("READY");
                badge.setTextColor(getColor(R.color.accent_badge));
                break;
            case CLEANING:
                badge.setText("CLEANING...");
                badge.setTextColor(getColor(R.color.text_primary));
                break;
            case CLEARED:
                badge.setText("CLEARED");
                badge.setTextColor(getColor(R.color.accent_badge));
                break;
            case ALREADY_CLEAN:
                badge.setText("ALREADY CLEAN");
                badge.setTextColor(getColor(R.color.accent_badge));
                break;
            case FAILED:
                badge.setText("FAILED");
                badge.setTextColor(getColor(android.R.color.holo_red_light));
                break;
            case SKIPPED:
                badge.setText("SKIPPED");
                badge.setTextColor(getColor(R.color.text_secondary));
                break;
        }
    }

    // =========================================================================
    // PilotController.BatchListener Callbacks
    // =========================================================================

    @Override
    public void onBatchProgress(final int currentIndex, final int total, final TargetApp currentApp) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                llBatchProgress.setVisibility(View.VISIBLE);
                tvBatchProgressMessage.setText(String.format(Locale.US,
                        "Processing %d of %d: %s...", currentIndex + 1, total, currentApp.getResolvedLabel()));
                updateAppRowDisplay(currentApp);
            }
        });
    }

    @Override
    public void onAppFinished(final TargetApp app, PilotController.Outcome outcome) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                updateAppRowDisplay(app);
            }
        });
    }

    @Override
    public void onBatchFinished(final List<TargetApp> batchResults, final String summary) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                isCleaningInProgress = false;
                btnCancelBatch.setVisibility(View.GONE);
                llBatchProgress.setVisibility(View.GONE);
                updateSelectionSummary();

                for (TargetApp app : batchResults) {
                    updateAppRowDisplay(app);
                }

                llBatchSummary.setVisibility(View.VISIBLE);
                tvBatchSummaryText.setText(summary);
            }
        });
    }

    @Override
    protected void onResume() {
        super.onResume();
        // If batch finished in background, refresh views
        if (!isCleaningInProgress && !targetApps.isEmpty()) {
            for (TargetApp app : targetApps) {
                updateAppRowDisplay(app);
            }
        }
    }
}
