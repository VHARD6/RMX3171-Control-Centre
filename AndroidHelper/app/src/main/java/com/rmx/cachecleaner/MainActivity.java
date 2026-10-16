package com.rmx.cachecleaner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.IntentFilter;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.Settings;
import android.view.View;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.SeekBar;
import android.widget.TextView;
import android.widget.Toast;

import java.util.Locale;

import rikka.shizuku.Shizuku;

/**
 * RMX Device Hub — Home Activity
 *
 * Core Modules:
 * 1. Primary Hardware Utility: Software Power Menu shortcut (GLOBAL_ACTION_POWER_DIALOG)
 * 2. Storage & Cache Maintenance:
 *    - Tier 1: Quick Clean All (Bulk system & quota-managed cache trim via Shizuku pm trim-caches 100G)
 *    - Tier 2: Guarded Multi-App Deep Clean (Private internal cache cleaner via Accessibility automation)
 * 3. System Shortcuts & Diagnostics:
 *    - Android Accessibility Settings shortcut
 *    - App Info & Storage management
 *    - Single-App Pilot Diagnostic (Instagram)
 */
public class MainActivity extends Activity {

    private static final int REQUEST_CODE_SHIZUKU_PERMISSION = 1003;

    // Header & Device Context
    private TextView tvDeviceContext;
    private TextView tvHeaderA11yStatus;
    private TextView tvHeaderShizukuStatus;

    // Primary Control 1: Power Menu Views
    private Button btnOpenPowerMenu;
    private LinearLayout llPowerA11yWarning;
    private TextView tvPowerA11yWarning;
    private Button btnPowerEnableA11y;

    // Edge Volume Views
    private TextView tvEdgeVolumeStatusBadge;
    private LinearLayout llEdgeVolumePermWarning;
    private Button btnGrantOverlayPerm;
    private Button btnToggleEdgeVolume;
    private LinearLayout llEdgeVolumeSettings;
    private TextView tvOpacityValueDisplay;
    private SeekBar seekHandleOpacity;
    private Button btnResetEdgePosition;
    private Button btnStopEdgeVolumeImmediate;

    // Primary Control 2: Tier 1 Quick Clean Views
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

    // Section 3: History Views
    private TextView tvMainHistoryCountBadge;
    private LinearLayout llMainHistoryEmpty;
    private LinearLayout llMainHistoryLatest;
    private TextView tvMainLatestType;
    private TextView tvMainLatestOutcome;
    private TextView tvMainLatestTime;
    private TextView tvMainLatestDelta;
    private TextView tvMainLatestSummary;
    private Button btnViewCleaningHistory;

    // System Utilities & Pilot Views
    private Button btnOpenSystemA11y;
    private Button btnAppInfo;
    private Button btnTestPilotInstagram;
    private LinearLayout llPilotResult;
    private TextView tvPilotOutcomeTitle;
    private TextView tvPilotOutcomeSummary;
    private TextView tvPilotCacheDetails;

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

    // Cross-process pilot receiver
    private BroadcastReceiver pilotReceiver;
    private boolean isPilotReceiverRegistered = false;

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
    protected void onStart() {
        super.onStart();
        registerPilotReceiver();
    }

    @Override
    protected void onStop() {
        super.onStop();
        unregisterPilotReceiver();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        unregisterPilotReceiver();
        PilotController.getInstance().setListener(null);
        try {
            Shizuku.removeRequestPermissionResultListener(permissionResultListener);
            Shizuku.removeBinderReceivedListener(binderReceivedListener);
            Shizuku.removeBinderDeadListener(binderDeadListener);
        } catch (Throwable ignored) {}
    }

    private void initViews() {
        // Header
        tvDeviceContext = findViewById(R.id.tv_device_context);
        tvHeaderA11yStatus = findViewById(R.id.tv_header_a11y_status);
        tvHeaderShizukuStatus = findViewById(R.id.tv_header_shizuku_status);

        // Power Menu Views
        btnOpenPowerMenu = findViewById(R.id.btn_open_power_menu);
        llPowerA11yWarning = findViewById(R.id.ll_power_a11y_warning);
        tvPowerA11yWarning = findViewById(R.id.tv_power_a11y_warning);
        btnPowerEnableA11y = findViewById(R.id.btn_power_enable_a11y);

        // Edge Volume Views
        tvEdgeVolumeStatusBadge = findViewById(R.id.tv_edge_volume_status_badge);
        llEdgeVolumePermWarning = findViewById(R.id.ll_edge_volume_perm_warning);
        btnGrantOverlayPerm = findViewById(R.id.btn_grant_overlay_perm);
        btnToggleEdgeVolume = findViewById(R.id.btn_toggle_edge_volume);
        llEdgeVolumeSettings = findViewById(R.id.ll_edge_volume_settings);
        tvOpacityValueDisplay = findViewById(R.id.tv_opacity_value_display);
        seekHandleOpacity = findViewById(R.id.seek_handle_opacity);
        btnResetEdgePosition = findViewById(R.id.btn_reset_edge_position);
        btnStopEdgeVolumeImmediate = findViewById(R.id.btn_stop_edge_volume_immediate);

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

        // Section 3: History Views
        tvMainHistoryCountBadge = findViewById(R.id.tv_main_history_count_badge);
        llMainHistoryEmpty = findViewById(R.id.ll_main_history_empty);
        llMainHistoryLatest = findViewById(R.id.ll_main_history_latest);
        tvMainLatestType = findViewById(R.id.tv_main_latest_type);
        tvMainLatestOutcome = findViewById(R.id.tv_main_latest_outcome);
        tvMainLatestTime = findViewById(R.id.tv_main_latest_time);
        tvMainLatestDelta = findViewById(R.id.tv_main_latest_delta);
        tvMainLatestSummary = findViewById(R.id.tv_main_latest_summary);
        btnViewCleaningHistory = findViewById(R.id.btn_view_cleaning_history);

        // System Utilities & Pilot Views
        btnOpenSystemA11y = findViewById(R.id.btn_open_system_a11y);
        btnAppInfo = findViewById(R.id.btn_app_info);
        btnTestPilotInstagram = findViewById(R.id.btn_test_pilot_instagram);
        llPilotResult = findViewById(R.id.ll_pilot_result);
        tvPilotOutcomeTitle = findViewById(R.id.tv_pilot_outcome_title);
        tvPilotOutcomeSummary = findViewById(R.id.tv_pilot_outcome_summary);
        tvPilotCacheDetails = findViewById(R.id.tv_pilot_cache_details);
    }

    private void setupListeners() {
        // Power Menu Action Button
        btnOpenPowerMenu.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                handlePowerMenuClick();
            }
        });

        // Power Menu A11y Enable Button
        btnPowerEnableA11y.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                PowerMenuController.openAccessibilitySettings(MainActivity.this);
            }
        });

        // Edge Volume Listeners
        if (btnGrantOverlayPerm != null) {
            btnGrantOverlayPerm.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    requestOverlayPermission();
                }
            });
        }

        if (btnToggleEdgeVolume != null) {
            btnToggleEdgeVolume.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    handleEdgeVolumeToggle();
                }
            });
        }

        if (seekHandleOpacity != null) {
            seekHandleOpacity.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener() {
                @Override
                public void onProgressChanged(SeekBar seekBar, int progress, boolean fromUser) {
                    if (fromUser) {
                        int clamped = Math.max(8, Math.min(50, progress));
                        float opacity = clamped / 100.0f;
                        ControlsClient.setHandleOpacity(MainActivity.this, opacity);
                        if (tvOpacityValueDisplay != null) {
                            tvOpacityValueDisplay.setText(clamped + "%");
                        }
                    }
                }

                @Override
                public void onStartTrackingTouch(SeekBar seekBar) {}

                @Override
                public void onStopTrackingTouch(SeekBar seekBar) {}
            });
        }

        if (btnResetEdgePosition != null) {
            btnResetEdgePosition.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    ControlsClient.resetHandlePosition(MainActivity.this);
                    Toast.makeText(MainActivity.this, "Handle position reset to middle.", Toast.LENGTH_SHORT).show();
                }
            });
        }

        if (btnStopEdgeVolumeImmediate != null) {
            btnStopEdgeVolumeImmediate.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    ControlsClient.enableEdgeVolume(MainActivity.this, false);
                    updateEdgeVolumeUi();
                    Toast.makeText(MainActivity.this, "Edge Volume Control stopped.", Toast.LENGTH_SHORT).show();
                }
            });
        }

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
                    PowerMenuController.openAccessibilitySettings(MainActivity.this);
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

        // System Utility: Direct Accessibility Settings Shortcut
        if (btnOpenSystemA11y != null) {
            btnOpenSystemA11y.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    PowerMenuController.openAccessibilitySettings(MainActivity.this);
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

        // Single-App Pilot (Instagram)
        if (btnTestPilotInstagram != null) {
            btnTestPilotInstagram.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    showPilotStep1ConfirmationDialog();
                }
            });
        }

        // Section 3: View Full History
        if (btnViewCleaningHistory != null) {
            btnViewCleaningHistory.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    startActivity(new Intent(MainActivity.this, CleaningHistoryActivity.class));
                }
            });
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateUiState();
        updateEdgeVolumeUi();
        updateHistorySection();
        handlePilotIntent(getIntent());

        Bundle state = ControlsClient.getControlsState(this);
        boolean isRunning = (state != null) ? state.getBoolean(ControlsProvider.KEY_IS_EDGE_VOLUME_RUNNING, false) : EdgeVolumeService.isServiceRunning();
        boolean isPrefEnabled = (state != null) ? state.getBoolean(ControlsProvider.KEY_IS_EDGE_VOLUME_ENABLED, false) : EdgeVolumePreferences.isEnabled(this);

        if (isPrefEnabled && Settings.canDrawOverlays(this) && !isRunning) {
            ControlsClient.enableEdgeVolume(this, true);
            updateEdgeVolumeUi();
        }
    }

    // =========================================================================
    // Edge Volume Control Methods
    // =========================================================================

    private void requestOverlayPermission() {
        try {
            Intent intent = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + getPackageName()));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            startActivity(intent);
        } catch (Throwable t) {
            Toast.makeText(this, "Could not open overlay settings: " + t.getMessage(), Toast.LENGTH_SHORT).show();
        }
    }

    private void handleEdgeVolumeToggle() {
        Bundle state = ControlsClient.getControlsState(this);
        boolean isRunning = (state != null) ? state.getBoolean(ControlsProvider.KEY_IS_EDGE_VOLUME_RUNNING, false) : EdgeVolumeService.isServiceRunning();
        boolean isPrefEnabled = (state != null) ? state.getBoolean(ControlsProvider.KEY_IS_EDGE_VOLUME_ENABLED, false) : EdgeVolumePreferences.isEnabled(this);

        if (isRunning || isPrefEnabled) {
            ControlsClient.enableEdgeVolume(this, false);
            updateEdgeVolumeUi();
            if (btnToggleEdgeVolume != null) {
                btnToggleEdgeVolume.postDelayed(new Runnable() {
                    @Override
                    public void run() {
                        updateEdgeVolumeUi();
                    }
                }, 150);
            }
            Toast.makeText(this, "Edge Volume Control disabled.", Toast.LENGTH_SHORT).show();
        } else {
            if (!Settings.canDrawOverlays(this)) {
                showOverlayPermissionRequiredDialog();
            } else {
                ControlsClient.enableEdgeVolume(this, true);
                updateEdgeVolumeUi();
                if (btnToggleEdgeVolume != null) {
                    btnToggleEdgeVolume.postDelayed(new Runnable() {
                        @Override
                        public void run() {
                            updateEdgeVolumeUi();
                        }
                    }, 200);
                }
                Toast.makeText(this, "Edge Volume Control enabled. Pull handle from right edge.", Toast.LENGTH_SHORT).show();
            }
        }
    }

    private void showOverlayPermissionRequiredDialog() {
        new AlertDialog.Builder(this)
                .setTitle(R.string.edge_volume_dialog_perm_title)
                .setMessage(R.string.edge_volume_dialog_perm_msg)
                .setPositiveButton("Open Settings", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        requestOverlayPermission();
                    }
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    private void updateEdgeVolumeUi() {
        boolean hasOverlayPerm = Settings.canDrawOverlays(this);
        Bundle state = ControlsClient.getControlsState(this);
        boolean isRunning = (state != null) ? state.getBoolean(ControlsProvider.KEY_IS_EDGE_VOLUME_RUNNING, false) : EdgeVolumeService.isServiceRunning();
        boolean isPrefEnabled = (state != null) ? state.getBoolean(ControlsProvider.KEY_IS_EDGE_VOLUME_ENABLED, false) : EdgeVolumePreferences.isEnabled(this);
        float opacity = (state != null) ? state.getFloat(ControlsProvider.KEY_HANDLE_OPACITY, 0.15f) : EdgeVolumePreferences.getHandleOpacity(this);

        if (llEdgeVolumePermWarning != null) {
            llEdgeVolumePermWarning.setVisibility(hasOverlayPerm ? View.GONE : View.VISIBLE);
        }

        if (tvEdgeVolumeStatusBadge != null) {
            if (isRunning) {
                tvEdgeVolumeStatusBadge.setText("ACTIVE");
                tvEdgeVolumeStatusBadge.setTextColor(getColor(R.color.accent_badge));
            } else if (isPrefEnabled && !hasOverlayPerm) {
                tvEdgeVolumeStatusBadge.setText("PERM REQUIRED");
                tvEdgeVolumeStatusBadge.setTextColor(getColor(R.color.status_warning));
            } else {
                tvEdgeVolumeStatusBadge.setText("STOPPED");
                tvEdgeVolumeStatusBadge.setTextColor(getColor(R.color.text_muted));
            }
        }

        if (btnToggleEdgeVolume != null) {
            if (isRunning) {
                btnToggleEdgeVolume.setText(R.string.btn_disable_edge_volume);
                btnToggleEdgeVolume.setBackgroundResource(R.drawable.btn_secondary_background);
                btnToggleEdgeVolume.setTextColor(getColor(R.color.text_primary));
            } else {
                btnToggleEdgeVolume.setText(R.string.btn_enable_edge_volume);
                btnToggleEdgeVolume.setBackgroundResource(R.drawable.btn_primary_background);
                btnToggleEdgeVolume.setTextColor(getColor(R.color.btn_primary_text));
            }
        }

        if (llEdgeVolumeSettings != null) {
            llEdgeVolumeSettings.setVisibility(isRunning ? View.VISIBLE : View.GONE);
        }

        if (seekHandleOpacity != null) {
            int opacityPct = Math.round(opacity * 100);
            seekHandleOpacity.setProgress(opacityPct);
            if (tvOpacityValueDisplay != null) {
                tvOpacityValueDisplay.setText(opacityPct + "%");
            }
        }
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        setIntent(intent);
        handlePilotIntent(intent);
    }

    private void handlePowerMenuClick() {
        PowerMenuController.Status status = PowerMenuController.getStatus(this);
        if (status == PowerMenuController.Status.SERVICE_DISABLED) {
            showAccessibilityRequiredDialog();
            return;
        }

        PowerMenuController.Result result = PowerMenuController.triggerPowerMenu(this);
        switch (result) {
            case SUCCESS:
                Toast.makeText(this, R.string.power_menu_opened, Toast.LENGTH_SHORT).show();
                break;
            case SERVICE_NOT_ENABLED:
                showAccessibilityRequiredDialog();
                break;
            case SERVICE_NOT_CONNECTED:
                Toast.makeText(this, R.string.power_menu_a11y_disconnected, Toast.LENGTH_LONG).show();
                updateUiState();
                break;
            case CLEANING_IN_PROGRESS:
                Toast.makeText(this, R.string.power_menu_cleaning_busy, Toast.LENGTH_SHORT).show();
                break;
            case THROTTLED:
                // Ignored quietly to prevent accidental duplicate actions
                break;
            case FAILED_SYSTEM_REJECTED:
                Toast.makeText(this, R.string.power_menu_failed, Toast.LENGTH_LONG).show();
                break;
        }
    }

    private void showAccessibilityRequiredDialog() {
        new AlertDialog.Builder(this)
                .setTitle("Accessibility Service Required")
                .setMessage("To open Android's power dialog without the physical button, RMX Device Hub requires its Accessibility Service to be active in Android Settings.\n\nPlease enable it and try again.")
                .setPositiveButton("Open Settings", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        PowerMenuController.openAccessibilitySettings(MainActivity.this);
                    }
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }

    private void updateUiState() {
        // 0. Update Device Context Header
        if (tvDeviceContext != null) {
            String mfg = Build.MANUFACTURER;
            String model = Build.MODEL;
            String brand = (mfg != null && mfg.length() > 0)
                    ? Character.toUpperCase(mfg.charAt(0)) + mfg.substring(1).toLowerCase(Locale.US)
                    : "Realme";
            tvDeviceContext.setText(brand + " " + model + " • Android " + Build.VERSION.RELEASE);
        }

        // 1. Update Accessibility Status
        PowerMenuController.Status powerStatus = PowerMenuController.getStatus(this);
        boolean isA11yActive = (powerStatus == PowerMenuController.Status.READY);

        if (tvHeaderA11yStatus != null) {
            if (isA11yActive) {
                tvHeaderA11yStatus.setText("ACTIVE");
                tvHeaderA11yStatus.setTextColor(getColor(R.color.accent_badge));
            } else if (powerStatus == PowerMenuController.Status.SERVICE_DISCONNECTED) {
                tvHeaderA11yStatus.setText("DISCONNECTED");
                tvHeaderA11yStatus.setTextColor(getColor(R.color.status_warning));
            } else {
                tvHeaderA11yStatus.setText("DISABLED");
                tvHeaderA11yStatus.setTextColor(getColor(R.color.status_error));
            }
        }

        if (tvTier2A11yStatus != null) {
            if (isA11yActive) {
                tvTier2A11yStatus.setText("ACTIVE");
                tvTier2A11yStatus.setTextColor(getColor(R.color.accent_badge));
                if (btnTier2OpenA11y != null) btnTier2OpenA11y.setVisibility(View.GONE);
            } else {
                tvTier2A11yStatus.setText("DISABLED");
                tvTier2A11yStatus.setTextColor(getColor(R.color.status_error));
                if (btnTier2OpenA11y != null) btnTier2OpenA11y.setVisibility(View.VISIBLE);
            }
        }

        // Power Menu Warning / State
        if (isA11yActive) {
            llPowerA11yWarning.setVisibility(View.GONE);
            btnOpenPowerMenu.setText(R.string.btn_power_menu);
        } else if (powerStatus == PowerMenuController.Status.SERVICE_DISCONNECTED) {
            llPowerA11yWarning.setVisibility(View.VISIBLE);
            tvPowerA11yWarning.setText(R.string.power_menu_a11y_disconnected);
            btnPowerEnableA11y.setText(R.string.btn_retry_a11y);
            btnPowerEnableA11y.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    updateUiState();
                }
            });
            btnOpenPowerMenu.setText(R.string.btn_power_menu);
        } else {
            llPowerA11yWarning.setVisibility(View.VISIBLE);
            tvPowerA11yWarning.setText(R.string.power_menu_a11y_required);
            btnPowerEnableA11y.setText(R.string.btn_enable_a11y);
            btnPowerEnableA11y.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    PowerMenuController.openAccessibilitySettings(MainActivity.this);
                }
            });
            btnOpenPowerMenu.setText("ENABLE ACCESSIBILITY FOR POWER MENU");
        }

        // 2. Update Shizuku Status for Tier 1
        boolean isInstalled = ShizukuCacheCleaner.isShizukuInstalled(this);
        boolean isRunning = ShizukuCacheCleaner.isShizukuRunning();
        boolean isAuthorized = ShizukuCacheCleaner.isShizukuAuthorized();

        if (tvHeaderShizukuStatus != null) {
            if (!isInstalled) {
                tvHeaderShizukuStatus.setText("MISSING");
                tvHeaderShizukuStatus.setTextColor(getColor(R.color.status_error));
            } else if (!isRunning) {
                tvHeaderShizukuStatus.setText("STOPPED");
                tvHeaderShizukuStatus.setTextColor(getColor(R.color.status_warning));
            } else if (!isAuthorized) {
                tvHeaderShizukuStatus.setText("UNAUTHORIZED");
                tvHeaderShizukuStatus.setTextColor(getColor(R.color.status_warning));
            } else {
                tvHeaderShizukuStatus.setText("READY");
                tvHeaderShizukuStatus.setTextColor(getColor(R.color.accent_badge));
            }
        }

        if (!isInstalled) {
            tvQuickShizukuStatus.setText("NOT INSTALLED");
            tvQuickShizukuStatus.setTextColor(getColor(R.color.status_error));
            btnQuickShizukuAction.setText("Install Shizuku App");
            btnQuickShizukuAction.setVisibility(View.VISIBLE);
            btnQuickCleanAll.setText("INSTALL SHIZUKU FOR QUICK CLEAN");
        } else if (!isRunning) {
            tvQuickShizukuStatus.setText("NOT RUNNING");
            tvQuickShizukuStatus.setTextColor(getColor(R.color.status_warning));
            btnQuickShizukuAction.setText("Open Shizuku & Start Service");
            btnQuickShizukuAction.setVisibility(View.VISIBLE);
            btnQuickCleanAll.setText("START SHIZUKU FOR QUICK CLEAN");
        } else if (!isAuthorized) {
            tvQuickShizukuStatus.setText("UNAUTHORIZED");
            tvQuickShizukuStatus.setTextColor(getColor(R.color.status_warning));
            btnQuickShizukuAction.setText("Authorize RMX Device Hub");
            btnQuickShizukuAction.setVisibility(View.VISIBLE);
            btnQuickCleanAll.setText("AUTHORIZE SHIZUKU FOR QUICK CLEAN");
        } else {
            tvQuickShizukuStatus.setText("READY");
            tvQuickShizukuStatus.setTextColor(getColor(R.color.accent_badge));
            btnQuickShizukuAction.setVisibility(View.GONE);
            btnQuickCleanAll.setText(R.string.btn_quick_clean_all);
        }
    }

    private void updateHistorySection() {
        CleaningHistoryRepository repo = CleaningHistoryRepository.getInstance(this);
        int count = repo.getRecordCount();
        CleaningHistoryRecord latest = repo.getLatestRecord();

        if (tvMainHistoryCountBadge != null) {
            tvMainHistoryCountBadge.setText(count + " RUNS");
        }

        if (btnViewCleaningHistory != null) {
            btnViewCleaningHistory.setText(count > 0
                    ? String.format(Locale.US, "View Full Cleaning History (%d)", count)
                    : "View Full Cleaning History");
        }

        if (count == 0 || latest == null) {
            if (llMainHistoryEmpty != null) llMainHistoryEmpty.setVisibility(View.VISIBLE);
            if (llMainHistoryLatest != null) llMainHistoryLatest.setVisibility(View.GONE);
            return;
        }

        if (llMainHistoryEmpty != null) llMainHistoryEmpty.setVisibility(View.GONE);
        if (llMainHistoryLatest != null) llMainHistoryLatest.setVisibility(View.VISIBLE);

        if (tvMainLatestType != null) {
            if (latest.getCleanType() == CleaningHistoryRecord.CleanType.QUICK_CLEAN) {
                tvMainLatestType.setText("QUICK CLEAN");
                tvMainLatestType.setTextColor(getColor(R.color.accent_primary));
            } else if (latest.getCleanType() == CleaningHistoryRecord.CleanType.DEEP_CLEAN) {
                tvMainLatestType.setText("GUARDED DEEP CLEAN");
                tvMainLatestType.setTextColor(getColor(R.color.badge_user_app));
            } else {
                tvMainLatestType.setText("TEST PILOT");
                tvMainLatestType.setTextColor(getColor(R.color.badge_preinstalled));
            }
        }

        if (tvMainLatestOutcome != null) {
            tvMainLatestOutcome.setText(latest.getOutcome().getDisplayName());
            switch (latest.getOutcome()) {
                case SUCCESS:
                    tvMainLatestOutcome.setTextColor(getColor(R.color.status_success));
                    break;
                case PARTIAL:
                case CANCELLED:
                    tvMainLatestOutcome.setTextColor(getColor(R.color.status_warning));
                    break;
                case FAILED:
                default:
                    tvMainLatestOutcome.setTextColor(getColor(R.color.status_error));
                    break;
            }
        }

        if (tvMainLatestTime != null) {
            tvMainLatestTime.setText(latest.getFormattedDate());
        }

        if (tvMainLatestDelta != null) {
            if (!latest.isStorageMeasured()) {
                tvMainLatestDelta.setText("Storage Unavailable");
                tvMainLatestDelta.setTextColor(getColor(R.color.text_muted));
            } else {
                long delta = latest.getBytesDelta();
                if (delta > 0) {
                    tvMainLatestDelta.setText("+" + StorageMeasurementUtil.formatBytes(delta));
                    tvMainLatestDelta.setTextColor(getColor(R.color.status_success));
                } else if (delta == 0) {
                    tvMainLatestDelta.setText("0 B (No Change)");
                    tvMainLatestDelta.setTextColor(getColor(R.color.text_secondary));
                } else {
                    tvMainLatestDelta.setText("-" + StorageMeasurementUtil.formatBytes(Math.abs(delta)));
                    tvMainLatestDelta.setTextColor(getColor(R.color.status_warning));
                }
            }
        }

        if (tvMainLatestSummary != null) {
            String summary = latest.getSummaryMessage();
            if (summary == null || summary.isEmpty()) summary = latest.getDetailNote();
            tvMainLatestSummary.setText(summary);
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
                // Record in history repository
                CleaningHistoryRecord.Outcome outcome = result.success
                        ? CleaningHistoryRecord.Outcome.SUCCESS
                        : CleaningHistoryRecord.Outcome.FAILED;
                CleaningHistoryRecord record = CleaningHistoryRecord.createQuickClean(
                        outcome,
                        result.durationMs,
                        result.freeBytesBefore,
                        result.freeBytesAfter,
                        result.summaryMessage
                );
                CleaningHistoryRepository.getInstance(MainActivity.this).addRecord(record);

                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        dismissQuickCleanProgressDialog();
                        renderQuickCleanResult(result);
                        updateHistorySection();
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
            tvQuickCleanStatus.setTextColor(getColor(R.color.status_error));
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
    // Single-App Pilot Diagnostic Methods (Instagram)
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

    private void registerPilotReceiver() {
        if (isPilotReceiverRegistered) return;
        pilotReceiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                if (intent == null) return;
                if (PilotController.ACTION_PILOT_FINISHED.equals(intent.getAction())) {
                    String outcomeStr = intent.getStringExtra(PilotController.EXTRA_OUTCOME);
                    String before = intent.getStringExtra(PilotController.EXTRA_CACHE_BEFORE);
                    String after = intent.getStringExtra(PilotController.EXTRA_CACHE_AFTER);
                    String summary = intent.getStringExtra(PilotController.EXTRA_SUMMARY);

                    final PilotController.PilotResult result = new PilotController.PilotResult();
                    if (outcomeStr != null) {
                        try {
                            result.outcome = PilotController.Outcome.valueOf(outcomeStr);
                        } catch (Throwable ignored) {}
                    }
                    if (before != null) result.cacheBefore = before;
                    if (after != null) result.cacheAfter = after;
                    if (summary != null) result.summaryMessage = summary;

                    runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            renderPilotResult(result);
                        }
                    });
                }
            }
        };
        IntentFilter filter = new IntentFilter(PilotController.ACTION_PILOT_FINISHED);
        registerReceiver(pilotReceiver, filter);
        isPilotReceiverRegistered = true;
    }

    private void unregisterPilotReceiver() {
        if (isPilotReceiverRegistered && pilotReceiver != null) {
            try {
                unregisterReceiver(pilotReceiver);
            } catch (Exception ignored) {}
            isPilotReceiverRegistered = false;
        }
    }

    private void handlePilotIntent(Intent intent) {
        if (intent != null && intent.hasExtra("pilot_outcome")) {
            String outcomeStr = intent.getStringExtra("pilot_outcome");
            String before = intent.getStringExtra("pilot_before");
            String after = intent.getStringExtra("pilot_after");
            String summary = intent.getStringExtra("pilot_summary");

            PilotController.PilotResult result = new PilotController.PilotResult();
            if (outcomeStr != null) {
                try {
                    result.outcome = PilotController.Outcome.valueOf(outcomeStr);
                } catch (Throwable ignored) {}
            }
            if (before != null) result.cacheBefore = before;
            if (after != null) result.cacheAfter = after;
            if (summary != null) result.summaryMessage = summary;

            renderPilotResult(result);
            intent.removeExtra("pilot_outcome");
        }
    }

    private void showPilotStep1ConfirmationDialog() {
        if (!ControlsClient.isA11yConnected(this)) {
            new AlertDialog.Builder(this)
                    .setTitle("Accessibility Service Required")
                    .setMessage("To test private cache cleaning, RMX Device Hub's Accessibility Service must be active in Android Settings.\n\nPlease enable it and try again.")
                    .setPositiveButton("Open Settings", new DialogInterface.OnClickListener() {
                        @Override
                        public void onClick(DialogInterface dialog, int which) {
                            PowerMenuController.openAccessibilitySettings(MainActivity.this);
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
        boolean started = false;
        if (SettingsInspectionAccessibilityService.isServiceRunning()) {
            started = PilotController.getInstance().startPilot(this);
        } else {
            started = ControlsClient.startPilot(this);
        }
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
                tvPilotOutcomeTitle.setTextColor(getColor(R.color.status_error));
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
