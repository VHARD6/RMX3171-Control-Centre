package com.rmx.cachecleaner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.ActivityNotFoundException;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.os.Environment;
import android.os.StatFs;
import android.os.storage.StorageManager;
import android.provider.Settings;
import android.system.OsConstants;
import android.view.LayoutInflater;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.TextView;
import android.widget.Toast;

import java.util.Locale;

import rikka.shizuku.Shizuku;

/**
 * RMX Cache Cleaner - Mobile Companion Application
 * 
 * Supports two complementary cleaning architectures:
 * 1. STANDARD CLEAN: Uses official StorageManager.ACTION_CLEAR_APP_CACHE
 *    to clear accessible external shared-storage app caches.
 * 2. DEEP CACHE CLEAN: Uses Shizuku's authorized shell Binder context
 *    to invoke PackageManager.deleteApplicationCacheFiles(...)
 *    clearing private app cache (/data/user/0/<pkg>/cache) without touching user data.
 */
public class MainActivity extends Activity {

    private static final int REQUEST_CODE_CLEAR_CACHE = 1001;
    private static final int REQUEST_CODE_MANAGE_STORAGE = 1002;
    private static final int REQUEST_CODE_SHIZUKU_PERMISSION = 1003;

    // Main Card Views
    private TextView tvBadge;
    private TextView tvExplanation;
    private TextView tvDetail;
    private RadioGroup rgScope;
    private RadioButton rbScopeUser;
    private RadioButton rbScopeAll;

    // Storage Result Views
    private LinearLayout llStorageStats;
    private TextView tvStatusTitle;
    private TextView tvStorageBefore;
    private TextView tvStorageAfter;
    private TextView tvStorageFreed;
    private View vSummaryDivider;
    private LinearLayout llDeepCleanStats;
    private TextView tvDeepCounts;
    private TextView tvDeepScope;
    private LinearLayout llScopeBreakdown;
    private TextView tvSummaryInternalVal;
    private TextView tvSummaryPrivateVal;

    // Action Buttons
    private Button btnFullClean;
    private Button btnDeepClean;
    private Button btnPrimaryAction;

    // Shizuku Privilege Card Views
    private LinearLayout llShizukuCard;
    private TextView tvShizukuBadge;
    private TextView tvShizukuDesc;
    private Button btnShizukuAction;

    // Secondary Action Button
    private Button btnAppInfo;

    // State Tracking
    private ShizukuCacheCleaner shizukuCleaner;
    private long freeBytesBefore = 0;
    private boolean isCombinedCleanRunning = false;
    private AlertDialog progressDialog = null;
    private ProgressBar pbProgress = null;
    private TextView tvProgressMsg = null;

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

        shizukuCleaner = new ShizukuCacheCleaner(this);

        initViews();
        setupListeners();

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
        tvBadge = findViewById(R.id.tv_badge);
        tvExplanation = findViewById(R.id.tv_explanation);
        tvDetail = findViewById(R.id.tv_detail);

        rgScope = findViewById(R.id.rg_scope);
        rbScopeUser = findViewById(R.id.rb_scope_user);
        rbScopeAll = findViewById(R.id.rb_scope_all);

        llStorageStats = findViewById(R.id.ll_storage_stats);
        tvStatusTitle = findViewById(R.id.tv_status_title);
        tvStorageBefore = findViewById(R.id.tv_storage_before);
        tvStorageAfter = findViewById(R.id.tv_storage_after);
        tvStorageFreed = findViewById(R.id.tv_storage_freed);
        vSummaryDivider = findViewById(R.id.v_summary_divider);

        llDeepCleanStats = findViewById(R.id.ll_deep_clean_stats);
        tvDeepCounts = findViewById(R.id.tv_deep_counts);
        tvDeepScope = findViewById(R.id.tv_deep_scope);

        llScopeBreakdown = findViewById(R.id.ll_scope_breakdown);
        tvSummaryInternalVal = findViewById(R.id.tv_summary_internal_val);
        tvSummaryPrivateVal = findViewById(R.id.tv_summary_private_val);

        btnFullClean = findViewById(R.id.btn_full_clean);
        btnDeepClean = findViewById(R.id.btn_deep_clean);
        btnPrimaryAction = findViewById(R.id.btn_primary_action);

        llShizukuCard = findViewById(R.id.ll_shizuku_card);
        tvShizukuBadge = findViewById(R.id.tv_shizuku_badge);
        tvShizukuDesc = findViewById(R.id.tv_shizuku_desc);
        btnShizukuAction = findViewById(R.id.btn_shizuku_action);

        btnAppInfo = findViewById(R.id.btn_app_info);
    }

    private void setupListeners() {
        // Scope selector warning on User + System Apps
        rgScope.setOnCheckedChangeListener(new RadioGroup.OnCheckedChangeListener() {
            @Override
            public void onCheckedChanged(RadioGroup group, int checkedId) {
                if (checkedId == R.id.rb_scope_all) {
                    new AlertDialog.Builder(MainActivity.this)
                        .setTitle(R.string.scope_system_warning_title)
                        .setMessage(R.string.scope_system_warning_msg)
                        .setPositiveButton(android.R.string.ok, null)
                        .setNegativeButton(android.R.string.cancel, new DialogInterface.OnClickListener() {
                            @Override
                            public void onClick(DialogInterface dialog, int which) {
                                rbScopeUser.setChecked(true);
                            }
                        })
                        .show();
                }
            }
        });

        // Combined Full Clean (Standard + Deep Clean)
        btnFullClean.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (!Environment.isExternalStorageManager()) {
                    requestManageExternalStoragePermission();
                    return;
                }
                showFullCleanConfirmationDialog();
            }
        });

        // Deep Clean Only
        btnDeepClean.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showDeepCleanConfirmationDialog();
            }
        });

        // Standard Clean
        btnPrimaryAction.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (Environment.isExternalStorageManager()) {
                    showStandardCleanConfirmationDialog();
                } else {
                    requestManageExternalStoragePermission();
                }
            }
        });

        // Shizuku Action Button
        btnShizukuAction.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                handleShizukuActionClick();
            }
        });

        // App Info
        btnAppInfo.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                openAppInfo();
            }
        });
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateUiState();
    }

    private void updateUiState() {
        boolean hasStoragePermission = Environment.isExternalStorageManager();
        boolean isInstalled = ShizukuCacheCleaner.isShizukuInstalled(this);
        boolean isRunning = ShizukuCacheCleaner.isShizukuRunning();
        boolean isAuthorized = ShizukuCacheCleaner.isShizukuAuthorized();

        // 1. Update Storage Permission state
        if (!hasStoragePermission) {
            tvBadge.setText(R.string.card_badge_permission);
            tvExplanation.setText(R.string.card_explanation_permission);
            tvDetail.setText(R.string.card_detail_permission);
            btnPrimaryAction.setText(R.string.btn_grant_storage);
            btnFullClean.setVisibility(View.GONE);
            btnDeepClean.setVisibility(View.GONE);
        } else {
            tvBadge.setText(R.string.card_badge_ready);
            tvExplanation.setText(R.string.card_explanation_ready);
            tvDetail.setText(R.string.card_detail_ready);

            if (isAuthorized) {
                btnFullClean.setVisibility(View.VISIBLE);
                btnDeepClean.setVisibility(View.VISIBLE);
                btnPrimaryAction.setText(R.string.btn_standard_clean);
            } else {
                btnFullClean.setVisibility(View.GONE);
                btnDeepClean.setVisibility(View.GONE);
                btnPrimaryAction.setText(R.string.btn_clean_cache);
            }
        }

        // 2. Update Shizuku Status Card
        if (!isInstalled) {
            tvShizukuBadge.setText(R.string.shizuku_badge_uninstalled);
            tvShizukuDesc.setText(R.string.shizuku_desc_uninstalled);
            btnShizukuAction.setText(R.string.shizuku_btn_install);
            btnShizukuAction.setVisibility(View.VISIBLE);
        } else if (!isRunning) {
            tvShizukuBadge.setText(R.string.shizuku_badge_not_running);
            tvShizukuDesc.setText(R.string.shizuku_desc_not_running);
            btnShizukuAction.setText(R.string.shizuku_btn_start);
            btnShizukuAction.setVisibility(View.VISIBLE);
        } else if (!isAuthorized) {
            tvShizukuBadge.setText(R.string.shizuku_badge_unauthorized);
            tvShizukuDesc.setText(R.string.shizuku_desc_unauthorized);
            btnShizukuAction.setText(R.string.shizuku_btn_authorize);
            btnShizukuAction.setVisibility(View.VISIBLE);
        } else {
            tvShizukuBadge.setText(R.string.shizuku_badge_ready);
            tvShizukuDesc.setText(R.string.shizuku_desc_ready);
            btnShizukuAction.setVisibility(View.GONE);
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

    private void requestManageExternalStoragePermission() {
        try {
            Intent intent = new Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION);
            intent.setData(Uri.parse("package:" + getPackageName()));
            startActivityForResult(intent, REQUEST_CODE_MANAGE_STORAGE);
        } catch (ActivityNotFoundException | SecurityException e1) {
            try {
                Intent fallback = new Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION);
                startActivityForResult(fallback, REQUEST_CODE_MANAGE_STORAGE);
            } catch (Exception e2) {
                Toast.makeText(this, R.string.toast_unable_to_open_settings, Toast.LENGTH_SHORT).show();
            }
        }
    }

    private void showStandardCleanConfirmationDialog() {
        new AlertDialog.Builder(this)
            .setTitle(R.string.dialog_title)
            .setMessage(R.string.dialog_clean_message)
            .setPositiveButton(R.string.dialog_positive, new DialogInterface.OnClickListener() {
                @Override
                public void onClick(DialogInterface dialog, int which) {
                    isCombinedCleanRunning = false;
                    launchSystemClearAppCache();
                }
            })
            .setNegativeButton(R.string.dialog_negative, null)
            .show();
    }

    private void showDeepCleanConfirmationDialog() {
        new AlertDialog.Builder(this)
            .setTitle(R.string.dialog_deep_title)
            .setMessage(R.string.dialog_deep_message)
            .setPositiveButton(R.string.dialog_deep_start, new DialogInterface.OnClickListener() {
                @Override
                public void onClick(DialogInterface dialog, int which) {
                    isCombinedCleanRunning = false;
                    startDeepCleanSweep();
                }
            })
            .setNegativeButton(R.string.dialog_negative, null)
            .show();
    }

    private void showFullCleanConfirmationDialog() {
        new AlertDialog.Builder(this)
            .setTitle(R.string.dialog_full_title)
            .setMessage(R.string.dialog_full_message)
            .setPositiveButton(R.string.dialog_positive, new DialogInterface.OnClickListener() {
                @Override
                public void onClick(DialogInterface dialog, int which) {
                    isCombinedCleanRunning = true;
                    launchSystemClearAppCache();
                }
            })
            .setNegativeButton(R.string.dialog_negative, null)
            .show();
    }

    private void launchSystemClearAppCache() {
        freeBytesBefore = getFreeStorageBytes();
        try {
            Intent intent = new Intent(StorageManager.ACTION_CLEAR_APP_CACHE);
            startActivityForResult(intent, REQUEST_CODE_CLEAR_CACHE);
        } catch (ActivityNotFoundException | SecurityException e) {
            Toast.makeText(this, "ACTION_CLEAR_APP_CACHE failed: " + e.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);

        if (requestCode == REQUEST_CODE_CLEAR_CACHE) {
            if (isCombinedCleanRunning && resultCode == RESULT_OK) {
                // Succeeded standard clean, now continue to deep clean sweep
                startDeepCleanSweep();
            } else {
                isCombinedCleanRunning = false;
                renderStandardCleanResult(resultCode);
            }
        } else if (requestCode == REQUEST_CODE_MANAGE_STORAGE) {
            updateUiState();
        }
    }

    private void startDeepCleanSweep() {
        if (freeBytesBefore == 0) {
            freeBytesBefore = getFreeStorageBytes();
        }

        int scope = rbScopeAll.isChecked() ? ShizukuCacheCleaner.SCOPE_USER_AND_SYSTEM : ShizukuCacheCleaner.SCOPE_USER_APPS;
        showProgressDialog();

        shizukuCleaner.startClean(scope,
            new ShizukuCacheCleaner.ProgressListener() {
                @Override
                public void onProgress(final int current, final int total, final String packageName,
                                       final String label, final int success, final int failed, final int skipped) {
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            updateProgressDialog(current, total, packageName, label, success, failed, skipped);
                        }
                    });
                }
            },
            new ShizukuCacheCleaner.CompletionListener() {
                @Override
                public void onComplete(final ShizukuCacheCleaner.CleanResult result) {
                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            dismissProgressDialog();
                            renderDeepCleanResult(result);
                        }
                    });
                }
            }
        );
    }

    private void showProgressDialog() {
        AlertDialog.Builder builder = new AlertDialog.Builder(this);
        builder.setTitle(R.string.progress_title);

        LinearLayout layout = new LinearLayout(this);
        layout.setOrientation(LinearLayout.VERTICAL);
        layout.setPadding(48, 24, 48, 24);

        pbProgress = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        pbProgress.setIndeterminate(false);
        pbProgress.setMax(100);
        layout.addView(pbProgress);

        tvProgressMsg = new TextView(this);
        tvProgressMsg.setPadding(0, 20, 0, 0);
        tvProgressMsg.setText("Preparing packages…");
        layout.addView(tvProgressMsg);

        builder.setView(layout);
        builder.setCancelable(false);
        builder.setNegativeButton(R.string.progress_btn_cancel, new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                shizukuCleaner.cancel();
            }
        });

        progressDialog = builder.create();
        progressDialog.show();
    }

    private void updateProgressDialog(int current, int total, String packageName, String label,
                                      int success, int failed, int skipped) {
        if (progressDialog != null && progressDialog.isShowing()) {
            if (pbProgress != null) {
                pbProgress.setMax(total);
                pbProgress.setProgress(current);
            }
            if (tvProgressMsg != null) {
                String text = String.format(Locale.US,
                    "Package %d / %d\n\nCurrent:\n%s\n(%s)\n\nSuccessful: %d   Failed: %d   Skipped: %d",
                    current, total, label, packageName, success, failed, skipped);
                tvProgressMsg.setText(text);
            }
        }
    }

    private void dismissProgressDialog() {
        if (progressDialog != null && progressDialog.isShowing()) {
            try {
                progressDialog.dismiss();
            } catch (Exception ignored) {}
        }
        progressDialog = null;
        pbProgress = null;
        tvProgressMsg = null;
    }

    private void renderStandardCleanResult(int resultCode) {
        long freeBytesAfter = getFreeStorageBytes();
        long beforeKb = freeBytesBefore / 1024L;
        long afterKb = freeBytesAfter / 1024L;

        double beforeGb = beforeKb / (1024.0 * 1024.0);
        double afterGb = afterKb / (1024.0 * 1024.0);
        double deltaMb = (afterKb - beforeKb) / 1024.0;

        boolean isSuccess = (resultCode == RESULT_OK);
        String statusText = isSuccess ? getString(R.string.status_completed) : getString(R.string.status_cancelled);

        String deltaStr = (deltaMb > 0)
                ? String.format(Locale.US, "+%.2f MB", deltaMb)
                : String.format(Locale.US, "%.2f MB", deltaMb);

        llStorageStats.setVisibility(View.VISIBLE);
        tvStatusTitle.setText(statusText);
        tvStorageBefore.setText(String.format(Locale.US, "Before free        %.2f GB", beforeGb));
        tvStorageAfter.setText(String.format(Locale.US, "After free         %.2f GB", afterGb));
        tvStorageFreed.setText(String.format(Locale.US, "Overall change     %s", deltaStr));

        llDeepCleanStats.setVisibility(View.GONE);
        vSummaryDivider.setVisibility(isSuccess ? View.VISIBLE : View.GONE);
        llScopeBreakdown.setVisibility(isSuccess ? View.VISIBLE : View.GONE);
        tvSummaryInternalVal.setText(R.string.summary_internal_val);
        tvSummaryPrivateVal.setText(R.string.summary_private_val_unaccessible);

        freeBytesBefore = 0;
    }

    private void renderDeepCleanResult(ShizukuCacheCleaner.CleanResult result) {
        long freeBytesAfter = getFreeStorageBytes();
        long beforeKb = freeBytesBefore / 1024L;
        long afterKb = freeBytesAfter / 1024L;

        double beforeGb = beforeKb / (1024.0 * 1024.0);
        double afterGb = afterKb / (1024.0 * 1024.0);
        double deltaMb = (afterKb - beforeKb) / 1024.0;

        String deltaStr = (deltaMb > 0)
                ? String.format(Locale.US, "+%.2f MB", deltaMb)
                : String.format(Locale.US, "%.2f MB", deltaMb);

        String title = isCombinedCleanRunning
                ? getString(R.string.status_full_completed)
                : getString(R.string.status_deep_completed);

        llStorageStats.setVisibility(View.VISIBLE);
        tvStatusTitle.setText(title);
        tvStorageBefore.setText(String.format(Locale.US, "Before free        %.2f GB", beforeGb));
        tvStorageAfter.setText(String.format(Locale.US, "After free         %.2f GB", afterGb));
        tvStorageFreed.setText(String.format(Locale.US, "Overall change     %s", deltaStr));

        // Deep Clean Metrics
        llDeepCleanStats.setVisibility(View.VISIBLE);
        String countsText = String.format(Locale.US,
                "Processed: %d\nCleared successfully: %d\nFailed: %d\nSkipped: %d",
                result.processed, result.successful, result.failed, result.skipped);
        tvDeepCounts.setText(countsText);

        String scopeStr = (result.scope == ShizukuCacheCleaner.SCOPE_USER_APPS)
                ? "Scope: User Apps"
                : "Scope: User + System Apps";
        tvDeepScope.setText(scopeStr);

        vSummaryDivider.setVisibility(View.VISIBLE);
        llScopeBreakdown.setVisibility(View.VISIBLE);

        if (isCombinedCleanRunning) {
            tvSummaryInternalVal.setText(R.string.summary_internal_val);
            tvSummaryPrivateVal.setText(R.string.summary_private_val_cleared);
        } else {
            tvSummaryInternalVal.setText("Untouched (Deep Clean mode)");
            tvSummaryPrivateVal.setText(R.string.summary_private_val_cleared);
        }

        isCombinedCleanRunning = false;
        freeBytesBefore = 0;
    }

    private long getFreeStorageBytes() {
        try {
            StatFs stat = new StatFs(Environment.getDataDirectory().getAbsolutePath());
            return stat.getAvailableBytes();
        } catch (Exception e) {
            return 0;
        }
    }

    private void openAppInfo() {
        try {
            Intent intent = new Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS);
            intent.setData(Uri.parse("package:" + getPackageName()));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(intent);
        } catch (Exception e) {
            Toast.makeText(this, R.string.toast_unable_to_open_app_info, Toast.LENGTH_SHORT).show();
        }
    }
}
