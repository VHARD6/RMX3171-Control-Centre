package com.rmx.cachecleaner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.content.Intent;
import android.net.Uri;
import android.os.Bundle;
import android.provider.Settings;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import java.util.Locale;

import rikka.shizuku.Shizuku;

/**
 * Phase 7: Unified Dual-Tier Cache Cleaning Main Hub.
 *
 * Tier 1: Quick Clean All
 * - Fast, system-wide, system-managed bulk cache trim using `pm trim-caches 100G` via Shizuku shell.
 * - Measures free storage before and after using StatFs.
 * - Honest delta reporting and scope disclosure.
 *
 * Tier 2: Deep Clean Selected Apps
 * - Comprehensive private internal cache cleaner using guarded Accessibility service automation.
 * - Discovers all installed user applications dynamically.
 * - Strictly protects application user data, logins, and databases.
 */
public class MainActivity extends Activity {

    private static final int REQUEST_CODE_SHIZUKU_PERMISSION = 1003;

    // Tier 1: Quick Clean Views
    private TextView tvQuickShizukuStatus;
    private Button btnQuickShizukuAction;
    private Button btnQuickCleanAll;
    private LinearLayout llQuickCleanResult;
    private TextView tvQuickCleanStatus;
    private TextView tvQuickStorageBefore;
    private TextView tvQuickStorageAfter;
    private TextView tvQuickStorageReclaimed;
    private TextView tvQuickDuration;
    private TextView tvQuickScopeNote;

    // Tier 2: Deep Clean Views
    private TextView tvTier2A11yStatus;
    private Button btnTier2OpenA11y;
    private Button btnOpenMultiAppCleaner;

    // Pilot Views (Phase 4.2)
    private Button btnTestPilotInstagram;
    private LinearLayout llPilotResult;
    private TextView tvPilotOutcomeTitle;
    private TextView tvPilotOutcomeSummary;
    private TextView tvPilotCacheDetails;

    // Secondary Action Button
    private Button btnAppInfo;

    // Progress Dialog for Quick Clean
    private AlertDialog quickCleanProgressDialog;

    private final Shizuku.OnRequestPermissionResultListener permissionResultListener =
            new Shizuku.OnRequestPermissionResultListener() {
                @Override
                public void onRequestPermissionResult(int requestCode, int grantResult) {
                    if (requestCode == REQUEST_CODE_SHIZUKU_PERMISSION) {
                        runOnUiThread(new Runnable() {
                            @Override
                            public void run() {
                                updateUiState();
                            }
                        });
                    }
                }
            };

    private final Shizuku.OnBinderReceivedListener binderReceivedListener =
            new Shizuku.OnBinderReceivedListener() {
                @Override
                public void onBinderReceived() {
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            updateUiState();
                        }
                    });
                }
            };

    private final Shizuku.OnBinderDeadListener binderDeadListener =
            new Shizuku.OnBinderDeadListener() {
                @Override
                public void onBinderDead() {
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            updateUiState();
                        }
                    });
                }
            };

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_main);

        initViews();
        setupListeners();
        setupPilotController();

        try {
            Shizuku.addRequestPermissionResultListener(permissionResultListener);
            Shizuku.addBinderReceivedListenerSticky(binderReceivedListener);
            Shizuku.addBinderDeadListener(binderDeadListener);
        } catch (Throwable ignored) {}
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        try {
            Shizuku.removeRequestPermissionResultListener(permissionResultListener);
            Shizuku.removeBinderReceivedListener(binderReceivedListener);
            Shizuku.removeBinderDeadListener(binderDeadListener);
        } catch (Throwable ignored) {}
    }

    private void initViews() {
        // Tier 1 Views
        tvQuickShizukuStatus = findViewById(R.id.tv_quick_shizuku_status);
        btnQuickShizukuAction = findViewById(R.id.btn_quick_shizuku_action);
        btnQuickCleanAll = findViewById(R.id.btn_quick_clean_all);
        llQuickCleanResult = findViewById(R.id.ll_quick_clean_result);
        tvQuickCleanStatus = findViewById(R.id.tv_quick_clean_status);
        tvQuickStorageBefore = findViewById(R.id.tv_quick_storage_before);
        tvQuickStorageAfter = findViewById(R.id.tv_quick_storage_after);
        tvQuickStorageReclaimed = findViewById(R.id.tv_quick_storage_reclaimed);
        tvQuickDuration = findViewById(R.id.tv_quick_duration);
        tvQuickScopeNote = findViewById(R.id.tv_quick_scope_note);

        // Tier 2 Views
        tvTier2A11yStatus = findViewById(R.id.tv_tier2_a11y_status);
        btnTier2OpenA11y = findViewById(R.id.btn_tier2_open_a11y);
        btnOpenMultiAppCleaner = findViewById(R.id.btn_open_multiapp_cleaner);

        // Pilot Views
        btnTestPilotInstagram = findViewById(R.id.btn_test_pilot_instagram);
        llPilotResult = findViewById(R.id.ll_pilot_result);
        tvPilotOutcomeTitle = findViewById(R.id.tv_pilot_outcome_title);
        tvPilotOutcomeSummary = findViewById(R.id.tv_pilot_outcome_summary);
        tvPilotCacheDetails = findViewById(R.id.tv_pilot_cache_details);

        // Secondary
        btnAppInfo = findViewById(R.id.btn_app_info);
    }

    private void setupListeners() {
        // Tier 1: Quick Clean Action Button
        btnQuickCleanAll.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (ShizukuQuickCleaner.isReadyForQuickClean(MainActivity.this)) {
                    showQuickCleanConfirmationDialog();
                } else {
                    handleShizukuActionClick();
                }
            }
        });

        // Tier 1: Shizuku Setup Button
        btnQuickShizukuAction.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                handleShizukuActionClick();
            }
        });

        // Tier 2: Open Accessibility Settings
        if (btnTier2OpenA11y != null) {
            btnTier2OpenA11y.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    openAccessibilitySettings();
                }
            });
        }

        // Tier 2: Open Multi-App Cleaner
        btnOpenMultiAppCleaner.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                startActivity(new Intent(MainActivity.this, AppSelectionActivity.class));
            }
        });

        // Single-App Pilot
        if (btnTestPilotInstagram != null) {
            btnTestPilotInstagram.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    showPilotStep1ConfirmationDialog();
                }
            });
        }

        // App Info
        if (btnAppInfo != null) {
            btnAppInfo.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    openAppInfo();
                }
            });
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateUiState();
        handlePilotIntent(getIntent());
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handlePilotIntent(intent);
    }

    private void updateUiState() {
        // 1. Update Shizuku Status for Tier 1
        boolean isInstalled = ShizukuCacheCleaner.isShizukuInstalled(this);
        boolean isRunning = ShizukuCacheCleaner.isShizukuRunning();
        boolean isAuthorized = ShizukuCacheCleaner.isShizukuAuthorized();

        if (!isInstalled) {
            tvQuickShizukuStatus.setText("NOT INSTALLED");
            tvQuickShizukuStatus.setTextColor(getColor(android.R.color.holo_red_light));
            btnQuickShizukuAction.setText("Install Shizuku App");
            btnQuickShizukuAction.setVisibility(View.VISIBLE);
            btnQuickCleanAll.setText("INSTALL SHIZUKU FOR QUICK CLEAN");
        } else if (!isRunning) {
            tvQuickShizukuStatus.setText("NOT RUNNING");
            tvQuickShizukuStatus.setTextColor(getColor(android.R.color.holo_orange_light));
            btnQuickShizukuAction.setText("Open Shizuku & Start Service");
            btnQuickShizukuAction.setVisibility(View.VISIBLE);
            btnQuickCleanAll.setText("START SHIZUKU FOR QUICK CLEAN");
        } else if (!isAuthorized) {
            tvQuickShizukuStatus.setText("UNAUTHORIZED");
            tvQuickShizukuStatus.setTextColor(getColor(android.R.color.holo_orange_light));
            btnQuickShizukuAction.setText("Authorize RMX Cache Cleaner");
            btnQuickShizukuAction.setVisibility(View.VISIBLE);
            btnQuickCleanAll.setText("AUTHORIZE SHIZUKU FOR QUICK CLEAN");
        } else {
            tvQuickShizukuStatus.setText("READY");
            tvQuickShizukuStatus.setTextColor(getColor(R.color.accent_badge));
            btnQuickShizukuAction.setVisibility(View.GONE);
            btnQuickCleanAll.setText(R.string.btn_quick_clean_all);
        }

        // 2. Update Accessibility Status for Tier 2
        boolean isA11yActive = SettingsInspectionAccessibilityService.isServiceRunning();
        if (isA11yActive) {
            tvTier2A11yStatus.setText("ACTIVE");
            tvTier2A11yStatus.setTextColor(getColor(R.color.accent_badge));
            btnTier2OpenA11y.setVisibility(View.GONE);
        } else {
            tvTier2A11yStatus.setText("DISABLED");
            tvTier2A11yStatus.setTextColor(getColor(android.R.color.holo_red_light));
            btnTier2OpenA11y.setVisibility(View.VISIBLE);
        }
    }

    private void handleShizukuActionClick() {
        if (!ShizukuCacheCleaner.isShizukuInstalled(this)) {
            ShizukuCacheCleaner.openShizukuWebsite(this);
        } else if (!ShizukuCacheCleaner.isShizukuRunning()) {
            ShizukuCacheCleaner.openShizukuApp(this);
        } else if (!ShizukuCacheCleaner.isShizukuAuthorized()) {
            ShizukuCacheCleaner.requestShizukuPermission(REQUEST_CODE_SHIZUKU_PERMISSION);
        }
    }

    private void showQuickCleanConfirmationDialog() {
        new AlertDialog.Builder(this)
                .setTitle(R.string.quick_clean_dialog_title)
                .setMessage(R.string.quick_clean_dialog_msg)
                .setPositiveButton("CONFIRM & CLEAN", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        startQuickCleanExecution();
                    }
                })
                .setNegativeButton("CANCEL", null)
                .show();
    }

    private void startQuickCleanExecution() {
        showQuickCleanProgressDialog();

        ShizukuQuickCleaner.executeQuickClean(this, new ShizukuQuickCleaner.QuickCleanCallback() {
            @Override
            public void onComplete(final ShizukuQuickCleaner.QuickCleanResult result) {
                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        dismissQuickCleanProgressDialog();
                        renderQuickCleanResult(result);
                    }
                });
            }
        });
    }

    private void showQuickCleanProgressDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setTitle("Running Quick Clean All…");

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(48, 24, 48, 24);

        ProgressBar pb = new ProgressBar(this);
        pb.setIndeterminate(true);
        layout.addView(pb);

        TextView tv = new TextView(this);
        tv.setPadding(0, 20, 0, 0);
        tv.setText("Trimming system quota caches via Shizuku shell (pm trim-caches 100G)…");
        tv.setTextColor(getColor(R.color.text_secondary));
        layout.addView(tv);

        builder.setView(layout);
        builder.setCancelable(false);

        quickCleanProgressDialog = builder.create();
        quickCleanProgressDialog.show();
    }

    private void dismissQuickCleanProgressDialog() {
        if (quickCleanProgressDialog != null && quickCleanProgressDialog.isShowing()) {
            try {
                quickCleanProgressDialog.dismiss();
            } catch (Throwable ignored) {}
        }
        quickCleanProgressDialog = null;
    }

    private void renderQuickCleanResult(ShizukuQuickCleaner.QuickCleanResult result) {
        if (result == null || llQuickCleanResult == null) return;
        llQuickCleanResult.setVisibility(View.VISIBLE);

        if (!result.success) {
            tvQuickCleanStatus.setText("QUICK CLEAN FAILED");
            tvQuickCleanStatus.setTextColor(getColor(android.R.color.holo_red_light));
            tvQuickStorageBefore.setText("Status: Error occurred during execution");
            tvQuickStorageAfter.setText(result.summaryMessage);
            tvQuickStorageReclaimed.setText("Reclaimed: 0 B");
            tvQuickDuration.setText("Duration: " + result.durationMs + " ms");
            tvQuickScopeNote.setText("Fallback: You can use Tier 2 Deep Clean below to clean private caches per-app.");
            return;
        }

        tvQuickCleanStatus.setText("QUICK CLEAN COMPLETED");
        tvQuickCleanStatus.setTextColor(getColor(R.color.accent_badge));

        double beforeGb = result.freeBytesBefore / (1024.0 * 1024.0 * 1024.0);
        double afterGb = result.freeBytesAfter / (1024.0 * 1024.0 * 1024.0);

        tvQuickStorageBefore.setText(String.format(Locale.US, "Free storage before:  %.2f GB", beforeGb));
        tvQuickStorageAfter.setText(String.format(Locale.US, "Free storage after:   %.2f GB", afterGb));

        if (result.bytesReclaimed > 0) {
            tvQuickStorageReclaimed.setText(String.format(Locale.US, "Storage reclaimed:    +%s",
                    ShizukuQuickCleaner.formatBytes(result.bytesReclaimed)));
            tvQuickStorageReclaimed.setTextColor(getColor(R.color.accent_badge));
        } else {
            tvQuickStorageReclaimed.setText("Storage reclaimed:    0 B (Optimal - no purgeable quota cache)");
            tvQuickStorageReclaimed.setTextColor(getColor(R.color.text_secondary));
        }

        tvQuickDuration.setText(String.format(Locale.US, "Execution duration:   %d ms (Exit code: %d)",
                result.durationMs, result.exitCode));

        tvQuickScopeNote.setText("Android system and quota-managed download caches were trimmed down to the system floor. Individual private app caches require Tier 2 Deep Clean.");
    }

    private void openAccessibilitySettings() {
        try {
            startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
        } catch (Throwable t) {
            Toast.makeText(this, R.string.toast_unable_to_open_settings, Toast.LENGTH_SHORT).show();
        }
    }

    private void openAppInfo() {
        try {
            Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            intent.setData(Uri.parse("package:" + getPackageName()));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(intent);
        } catch (Throwable t) {
            Toast.makeText(this, R.string.toast_unable_to_open_app_info, Toast.LENGTH_SHORT).show();
        }
    }

    // =========================================================================
    // Phase 4.2: Single-App Pilot Methods
    // =========================================================================

    private void setupPilotController() {
        PilotController.getInstance().setListener(new PilotController.PilotListener() {
            @Override
            public void onPilotStateChanged(PilotController.State state, String message) {}

            @Override
            public void onPilotFinished(final PilotController.PilotResult result) {
                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        renderPilotResult(result);
                    }
                });
            }
        });
    }

    private void handlePilotIntent(Intent intent) {
        if (intent != null && intent.hasExtra("pilot_outcome")) {
            PilotController.PilotResult result = PilotController.getInstance().getCurrentResult();
            renderPilotResult(result);
            intent.removeExtra("pilot_outcome");
        }
    }

    private void showPilotStep1ConfirmationDialog() {
        if (!SettingsInspectionAccessibilityService.isServiceRunning()) {
            new AlertDialog.Builder(this)
                    .setTitle("Accessibility Service Required")
                    .setMessage("To test private cache cleaning, RMX Cache Cleaner's Accessibility Probe must be active in Android Settings.\n\nPlease enable it and try again.")
                    .setPositiveButton("Open Settings", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            openAccessibilitySettings();
                        }
                    })
                    .setNegativeButton(android.R.string.cancel, null)
                    .show();
            return;
        }

        new AlertDialog.Builder(this)
                .setTitle(R.string.pilot_step1_dialog_title)
                .setMessage(R.string.pilot_step1_dialog_msg)
                .setPositiveButton(R.string.pilot_step1_btn_proceed, new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        startPilotTest();
                    }
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    private void startPilotTest() {
        if (llPilotResult != null) {
            llPilotResult.setVisibility(View.GONE);
        }
        boolean started = PilotController.getInstance().startPilot(this);
        if (!started) {
            Toast.makeText(this, "Failed to initiate pilot test.", Toast.LENGTH_SHORT).show();
        }
    }

    private void renderPilotResult(PilotController.PilotResult result) {
        if (llPilotResult == null || result == null || result.outcome == PilotController.Outcome.NONE) {
            return;
        }
        llPilotResult.setVisibility(View.VISIBLE);

        switch (result.outcome) {
            case ALREADY_CLEAN:
                tvPilotOutcomeTitle.setText("PILOT OUTCOME: ALREADY_CLEAN");
                tvPilotOutcomeTitle.setTextColor(getColor(R.color.accent_badge));
                tvPilotOutcomeSummary.setText(result.summaryMessage);
                tvPilotCacheDetails.setText(String.format(Locale.US, "Cache: %s  |  'Clear cache' disabled", result.cacheBefore));
                break;

            case SETTINGS_UI_CONFIRMED:
                tvPilotOutcomeTitle.setText("PILOT OUTCOME: SETTINGS_UI_CONFIRMED");
                tvPilotOutcomeTitle.setTextColor(getColor(R.color.accent_badge));
                tvPilotOutcomeSummary.setText(result.summaryMessage);
                tvPilotCacheDetails.setText(String.format(Locale.US, "Before: %s  |  After: %s", result.cacheBefore, result.cacheAfter));
                break;

            case ACTION_ATTEMPTED_UNVERIFIED:
                tvPilotOutcomeTitle.setText("PILOT OUTCOME: ACTION_ATTEMPTED_UNVERIFIED");
                tvPilotOutcomeTitle.setTextColor(getColor(R.color.text_primary));
                tvPilotOutcomeSummary.setText(result.summaryMessage);
                tvPilotCacheDetails.setText(String.format(Locale.US, "Before: %s  |  After: %s", result.cacheBefore, result.cacheAfter));
                break;

            case FAILED:
                tvPilotOutcomeTitle.setText("PILOT OUTCOME: FAILED");
                tvPilotOutcomeTitle.setTextColor(getColor(android.R.color.holo_red_light));
                tvPilotOutcomeSummary.setText(result.summaryMessage);
                tvPilotCacheDetails.setText("Safety abort or error. Check Logcat (tag: RMX_PILOT).");
                break;

            case CANCELLED:
                tvPilotOutcomeTitle.setText("PILOT OUTCOME: CANCELLED");
                tvPilotOutcomeTitle.setTextColor(getColor(R.color.text_secondary));
                tvPilotOutcomeSummary.setText(result.summaryMessage);
                tvPilotCacheDetails.setText("No cache or data modifications were made.");
                break;

            default:
                llPilotResult.setVisibility(View.GONE);
                break;
        }
    }
}
