package com.rmx.cachecleaner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.graphics.drawable.Drawable;
import android.net.Uri;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.util.Log;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.CompoundButton;
import android.widget.EditText;
import android.widget.ImageButton;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;
import android.widget.Toast;

import androidx.annotation.NonNull;
import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * Phase 6 & Phase 7: Multi-App Selection and Batch Cleaning Activity.
 * Displays all installed applications using RecyclerView for 60 FPS recycling,
 * dynamically detects future app installations via BroadcastReceiver and onResume discovery,
 * provides manual on-demand refresh, handles user selection, upfront batch confirmation,
 * monitors guarded sequential execution, provides live accessibility service status,
 * and transparently reports results.
 */
public class AppSelectionActivity extends Activity implements PilotController.BatchListener, PilotController.PilotListener {

    private static final String TAG = "RMX_DYNAMIC_DISCOVERY";
    private static final ExecutorService ICON_LOADER_EXECUTOR = Executors.newFixedThreadPool(4);
    private static final Map<String, Drawable> ICON_CACHE = new ConcurrentHashMap<>();

    // Accessibility Service Status Card
    private LinearLayout llA11yStatusCard;
    private TextView tvA11yStatusBadge;
    private TextView tvA11yStatusDesc;
    private Button btnOpenA11ySettings;

    // Selection & Filter Controls
    private ImageButton btnRefreshApps;
    private EditText etSearchApps;
    private RecyclerView rvTargetApps;
    private TargetAppAdapter adapter;
    private TextView tvSelectionCount;
    private Button btnSelectAll;
    private Button btnDeselectAll;
    private Button btnInvertSelection;
    private TextView tvSelectionHint;
    private Button btnCleanSelected;
    private Button btnCancelBatch;
    private TextView tvEmptyNotice;

    // Batch progress & summary containers
    private LinearLayout llBatchProgress;
    private TextView tvBatchProgressTitle;
    private TextView tvBatchProgressMessage;
    private TextView tvBatchProgressStep;
    private ProgressBar pbBatchProgress;
    private LinearLayout llBatchSummary;
    private TextView tvBatchSummaryTitle;
    private TextView tvBatchSummaryText;

    private List<TargetApp> targetApps = new ArrayList<>();
    private String currentSearchQuery = "";
    private boolean isCleaningInProgress = false;

    // Dynamic package installation detection
    private BroadcastReceiver packageChangeReceiver;
    private boolean isReceiverRegistered = false;
    private boolean needsRefreshAfterBatch = false;

    // Authoritative package-based selection manager
    private final AppSelectionManager selectionManager = new AppSelectionManager();

    // Measurement & History Tracking
    private long batchStartTime = 0;
    private long batchStorageBefore = -1;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_app_selection);

        PilotController.getInstance().setListener(this);
        initViews();
        loadSupportedApps();
    }

    @Override
    protected void onStart() {
        super.onStart();
        registerPackageReceiver();
    }

    @Override
    protected void onStop() {
        super.onStop();
        unregisterPackageReceiver();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        unregisterPackageReceiver();
        PilotController.getInstance().setListener(null);
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

        btnRefreshApps = findViewById(R.id.btn_refresh_apps);
        if (btnRefreshApps != null) {
            btnRefreshApps.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (isCleaningInProgress) {
                        Toast.makeText(AppSelectionActivity.this, "Cannot refresh while cleaning is in progress.", Toast.LENGTH_SHORT).show();
                        return;
                    }
                    refreshAppInventory(true);
                }
            });
        }

        // Accessibility card views
        llA11yStatusCard = findViewById(R.id.ll_a11y_status_card);
        tvA11yStatusBadge = findViewById(R.id.tv_a11y_status_badge);
        tvA11yStatusDesc = findViewById(R.id.tv_a11y_status_desc);
        btnOpenA11ySettings = findViewById(R.id.btn_open_a11y_settings);

        if (btnOpenA11ySettings != null) {
            btnOpenA11ySettings.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    try {
                        startActivity(new Intent(Settings.ACTION_ACCESSIBILITY_SETTINGS));
                    } catch (Exception e) {
                        Toast.makeText(AppSelectionActivity.this, "Unable to open Settings.", Toast.LENGTH_SHORT).show();
                    }
                }
            });
        }

        etSearchApps = findViewById(R.id.et_search_apps);
        rvTargetApps = findViewById(R.id.rv_target_apps);
        tvSelectionCount = findViewById(R.id.tv_selection_count);
        btnSelectAll = findViewById(R.id.btn_select_all);
        btnDeselectAll = findViewById(R.id.btn_deselect_all);
        btnInvertSelection = findViewById(R.id.btn_invert_selection);
        tvSelectionHint = findViewById(R.id.tv_selection_hint);
        btnCleanSelected = findViewById(R.id.btn_clean_selected);
        btnCancelBatch = findViewById(R.id.btn_cancel_batch);
        tvEmptyNotice = findViewById(R.id.tv_empty_notice);

        llBatchProgress = findViewById(R.id.ll_batch_progress);
        tvBatchProgressTitle = findViewById(R.id.tv_batch_progress_title);
        tvBatchProgressMessage = findViewById(R.id.tv_batch_progress_message);
        tvBatchProgressStep = findViewById(R.id.tv_batch_progress_step);
        pbBatchProgress = findViewById(R.id.pb_batch_progress);

        llBatchSummary = findViewById(R.id.ll_batch_summary);
        tvBatchSummaryTitle = findViewById(R.id.tv_batch_summary_title);
        tvBatchSummaryText = findViewById(R.id.tv_batch_summary_text);

        // Configure RecyclerView
        rvTargetApps.setLayoutManager(new LinearLayoutManager(this));
        adapter = new TargetAppAdapter();
        rvTargetApps.setAdapter(adapter);

        if (etSearchApps != null) {
            final Handler searchDebounceHandler = new Handler(Looper.getMainLooper());
            final Runnable searchRunnable = new Runnable() {
                @Override
                public void run() {
                    applySearchFilter();
                }
            };

            etSearchApps.addTextChangedListener(new android.text.TextWatcher() {
                @Override
                public void beforeTextChanged(CharSequence s, int start, int count, int after) {}

                @Override
                public void onTextChanged(CharSequence s, int start, int before, int count) {
                    currentSearchQuery = (s != null) ? s.toString().trim() : "";
                    searchDebounceHandler.removeCallbacks(searchRunnable);
                    searchDebounceHandler.postDelayed(searchRunnable, 100);
                }

                @Override
                public void afterTextChanged(android.text.Editable s) {}
            });
        }

        if (btnSelectAll != null) {
            btnSelectAll.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    setSelectionState(true);
                }
            });
        }

        if (btnDeselectAll != null) {
            btnDeselectAll.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    setSelectionState(false);
                }
            });
        }

        if (btnInvertSelection != null) {
            btnInvertSelection.setOnClickListener(new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    invertSelection();
                }
            });
        }

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

        updateAccessibilityStatusBanner();
    }

    private void updateAccessibilityStatusBanner() {
        boolean running = SettingsInspectionAccessibilityService.isServiceRunning();
        if (tvA11yStatusBadge == null || tvA11yStatusDesc == null || btnOpenA11ySettings == null) return;

        if (running) {
            tvA11yStatusBadge.setText("ACTIVE");
            tvA11yStatusBadge.setTextColor(getColor(R.color.accent_badge));
            tvA11yStatusDesc.setText("Accessibility service is active and ready for guarded Settings automation.");
            btnOpenA11ySettings.setVisibility(View.GONE);
        } else {
            tvA11yStatusBadge.setText("DISABLED");
            tvA11yStatusBadge.setTextColor(getColor(android.R.color.holo_red_light));
            tvA11yStatusDesc.setText("Required for guarded cache cleaning. Enable RMX Cache Cleaner in Android Accessibility settings.");
            btnOpenA11ySettings.setVisibility(View.VISIBLE);
        }
    }

    private void loadSupportedApps() {
        refreshAppInventory(false);
    }

    /**
     * Dynamically queries PackageManager to refresh installed apps.
     * Preserves active user selection by package name for persisting apps and keeps newly discovered apps unselected.
     */
    private void refreshAppInventory(boolean notifyUser) {
        if (isCleaningInProgress) {
            needsRefreshAfterBatch = true;
            return;
        }

        PackageManager pm = getPackageManager();
        List<TargetApp> freshApps = SupportedAppsRegistry.getInstalledEligibleApps(pm);

        // Track previous execution statuses
        Map<String, TargetApp.Status> prevStatuses = new HashMap<>();
        Map<String, String> prevDetails = new HashMap<>();
        Map<String, String> prevCacheBefore = new HashMap<>();
        Map<String, String> prevCacheAfter = new HashMap<>();

        if (targetApps != null) {
            for (TargetApp oldApp : targetApps) {
                String pkg = oldApp.getPackageName();
                prevStatuses.put(pkg, oldApp.getStatus());
                prevDetails.put(pkg, oldApp.getStatusDetail());
                prevCacheBefore.put(pkg, oldApp.getCacheBefore());
                prevCacheAfter.put(pkg, oldApp.getCacheAfter());
            }
        }

        int newAppsCount = 0;
        if (!selectionManager.isInitialized()) {
            // First initialization: all selectable eligible apps start selected by default
            selectionManager.initializeDefaultSelection(freshApps);
        } else {
            // Subsequent runs: preserve exact selection by package name
            for (TargetApp freshApp : freshApps) {
                String pkg = freshApp.getPackageName();
                if (freshApp.isProtected()) {
                    selectionManager.setSelected(pkg, false, true);
                    freshApp.setSelected(false);
                } else if (selectionManager.isSelected(pkg, false)) {
                    // Previously selected installed app
                    freshApp.setSelected(true);
                } else if (prevStatuses.containsKey(pkg)) {
                    // Previously known installed app that was unselected by user
                    freshApp.setSelected(false);
                } else {
                    // Newly discovered app: default to unselected for batch safety
                    newAppsCount++;
                    selectionManager.setSelected(pkg, false, false);
                    freshApp.setSelected(false);
                    if (!freshApp.isProtected()) {
                        freshApp.setStatus(TargetApp.Status.PENDING);
                        freshApp.setStatusDetail("Newly installed • Ready for cleaning");
                    }
                }

                // Restore cleaning status if processed in this session
                TargetApp.Status prevStatus = prevStatuses.get(pkg);
                if (prevStatus != null && prevStatus != TargetApp.Status.PENDING) {
                    freshApp.setStatus(prevStatus);
                    freshApp.setStatusDetail(prevDetails.get(pkg));
                    freshApp.setCacheBefore(prevCacheBefore.get(pkg));
                    freshApp.setCacheAfter(prevCacheAfter.get(pkg));
                }
            }
        }

        // Clean up selectedPackages for any apps that are no longer installed
        Set<String> installedPkgs = new HashSet<>();
        for (TargetApp app : freshApps) {
            installedPkgs.add(app.getPackageName());
        }
        selectionManager.retainOnly(installedPkgs);

        targetApps = freshApps;
        applySearchFilter();

        if (notifyUser) {
            String msg = (newAppsCount > 0)
                    ? String.format(Locale.US, "Refreshed: %d new app(s) discovered (%d total).", newAppsCount, targetApps.size())
                    : String.format(Locale.US, "Refreshed: %d installed apps up to date.", targetApps.size());
            Toast.makeText(this, msg, Toast.LENGTH_SHORT).show();
        }
    }

    private void applySearchFilter() {
        if (targetApps.isEmpty()) {
            tvEmptyNotice.setVisibility(View.VISIBLE);
            tvEmptyNotice.setText("No installed applications found on this device.");
            btnCleanSelected.setEnabled(false);
            if (btnSelectAll != null) btnSelectAll.setEnabled(false);
            if (btnDeselectAll != null) btnDeselectAll.setEnabled(false);
            if (btnInvertSelection != null) btnInvertSelection.setEnabled(false);
            tvSelectionCount.setText("Installed Apps (0)");
            adapter.setApps(Collections.<TargetApp>emptyList());
            return;
        }

        List<TargetApp> displayedApps = SupportedAppsRegistry.filterApps(targetApps, currentSearchQuery);

        if (displayedApps.isEmpty()) {
            tvEmptyNotice.setVisibility(View.VISIBLE);
            tvEmptyNotice.setText("No installed applications matched \"" + currentSearchQuery + "\"");
        } else {
            tvEmptyNotice.setVisibility(View.GONE);
        }

        adapter.setApps(displayedApps);
        updateSelectionSummary(displayedApps.size());
    }

    private void updateSelectionSummary() {
        int count = currentSearchQuery.isEmpty() ? targetApps.size() : SupportedAppsRegistry.filterApps(targetApps, currentSearchQuery).size();
        updateSelectionSummary(count);
    }

    private void updateSelectionSummary(int displayedCount) {
        int selected = selectionManager.countSelectedIn(targetApps);
        int selectableCount = 0;
        for (TargetApp app : targetApps) {
            if (!app.isProtected()) {
                selectableCount++;
            }
        }
        selectionManager.syncAppSelectionState(targetApps);

        if (currentSearchQuery.isEmpty()) {
            tvSelectionCount.setText(String.format(Locale.US,
                    "Installed Apps (%d of %d selectable selected • %d total)", selected, selectableCount, targetApps.size()));
        } else {
            tvSelectionCount.setText(String.format(Locale.US,
                    "Installed Apps (%d of %d selectable selected • %d showing of %d total)", selected, selectableCount, displayedCount, targetApps.size()));
        }

        if (selected == 0) {
            btnCleanSelected.setText("SELECT APPS TO CLEAN");
            btnCleanSelected.setEnabled(false);
            if (tvSelectionHint != null) {
                tvSelectionHint.setVisibility(View.VISIBLE);
                tvSelectionHint.setText("Select at least 1 application to begin cleaning.");
            }
        } else {
            btnCleanSelected.setText(String.format(Locale.US, "CLEAN SELECTED APPS (%d)", selected));
            btnCleanSelected.setEnabled(!isCleaningInProgress);
            if (tvSelectionHint != null) {
                tvSelectionHint.setVisibility(View.GONE);
            }
        }

        boolean hasSelectable = selectableCount > 0;
        if (btnSelectAll != null) btnSelectAll.setEnabled(!isCleaningInProgress && hasSelectable);
        if (btnDeselectAll != null) btnDeselectAll.setEnabled(!isCleaningInProgress && hasSelectable);
        if (btnInvertSelection != null) btnInvertSelection.setEnabled(!isCleaningInProgress && hasSelectable);
    }

    private void setSelectionState(boolean selected) {
        if (isCleaningInProgress) return;
        List<TargetApp> affected = currentSearchQuery.isEmpty()
                ? targetApps
                : SupportedAppsRegistry.filterApps(targetApps, currentSearchQuery);

        if (selected) {
            selectionManager.selectAll(affected);
        } else {
            selectionManager.deselectAll(affected);
        }
        adapter.notifyDataSetChanged();
        updateSelectionSummary(affected.size());
    }

    private void invertSelection() {
        if (isCleaningInProgress) return;
        List<TargetApp> affected = currentSearchQuery.isEmpty()
                ? targetApps
                : SupportedAppsRegistry.filterApps(targetApps, currentSearchQuery);

        selectionManager.invertSelection(affected);
        adapter.notifyDataSetChanged();
        updateSelectionSummary(affected.size());
    }

    private void setSelectionControlsEnabled(boolean enabled) {
        if (etSearchApps != null) etSearchApps.setEnabled(enabled);
        if (btnSelectAll != null) btnSelectAll.setEnabled(enabled && !targetApps.isEmpty());
        if (btnDeselectAll != null) btnDeselectAll.setEnabled(enabled && !targetApps.isEmpty());
        if (btnInvertSelection != null) btnInvertSelection.setEnabled(enabled && !targetApps.isEmpty());
        if (btnRefreshApps != null) btnRefreshApps.setEnabled(enabled);
        adapter.setSelectable(enabled);
    }

    private void showBatchConfirmationDialog() {
        final List<TargetApp> selectedApps = new ArrayList<>();
        for (TargetApp app : targetApps) {
            if (selectionManager.isSelected(app.getPackageName(), app.isProtected())) {
                selectedApps.add(app);
            }
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
        msg.append("The following ").append(selectedApps.size())
           .append(" application(s) will be processed sequentially:\n\n");
        for (int i = 0; i < selectedApps.size(); i++) {
            TargetApp app = selectedApps.get(i);
            msg.append(i + 1).append(". ")
               .append(app.getResolvedLabel())
               .append("\n    Package: ")
               .append(app.getPackageName())
               .append("\n");
        }
        msg.append("\n• Only private cache will be cleared.")
           .append("\n• Each app undergoes dual identity verification.")
           .append("\n• Protected components and user data are never touched.");

        new AlertDialog.Builder(this)
                .setTitle("Confirm Batch Cleaning")
                .setMessage(msg.toString())
                .setPositiveButton("CONFIRM & START", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        startBatchCleaning(selectedApps);
                    }
                })
                .setNegativeButton("CANCEL", null)
                .show();
    }

    private void startBatchCleaning(List<TargetApp> selectedApps) {
        isCleaningInProgress = true;
        batchStartTime = System.currentTimeMillis();
        batchStorageBefore = StorageMeasurementUtil.getAvailableStorageBytes();
        setSelectionControlsEnabled(false);
        btnCleanSelected.setEnabled(false);
        btnCancelBatch.setVisibility(View.VISIBLE);
        llBatchProgress.setVisibility(View.VISIBLE);
        llBatchSummary.setVisibility(View.GONE);

        // Mark apps queued/skipped
        for (TargetApp app : targetApps) {
            if (app.isProtected()) {
                continue;
            }
            if (selectionManager.isSelected(app.getPackageName(), false)) {
                app.setSelected(true);
                app.setStatus(TargetApp.Status.PENDING);
                app.setStatusDetail("Queued for cleaning");
            } else {
                app.setSelected(false);
                app.setStatus(TargetApp.Status.SKIPPED);
                app.setStatusDetail("Not selected");
            }
            adapter.updateApp(app);
        }

        boolean started = PilotController.getInstance().startBatch(this, selectedApps, this);
        if (!started) {
            isCleaningInProgress = false;
            setSelectionControlsEnabled(true);
            btnCleanSelected.setEnabled(true);
            btnCancelBatch.setVisibility(View.GONE);
            llBatchProgress.setVisibility(View.GONE);
            Toast.makeText(this, "Failed to start batch cleaner.", Toast.LENGTH_SHORT).show();
        }
    }

    private void updateBadgeAppearance(TextView badge, TargetApp.Status status) {
        if (badge == null || status == null) return;
        switch (status) {
            case PROTECTED:
                badge.setText("PROTECTED");
                badge.setTextColor(getColor(R.color.badge_protected));
                break;
            case DISABLED:
                badge.setText("DISABLED");
                badge.setTextColor(getColor(R.color.badge_disabled));
                break;
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
            case CANCELLED:
                badge.setText("CANCELLED");
                badge.setTextColor(getColor(android.R.color.holo_orange_light));
                break;
        }
    }

    // =========================================================================
    // Dynamic Package Installation Broadcast Receiver
    // =========================================================================

    private void registerPackageReceiver() {
        if (isReceiverRegistered) return;
        packageChangeReceiver = new BroadcastReceiver() {
            @Override
            public void onReceive(Context context, Intent intent) {
                if (intent == null) return;
                String action = intent.getAction();
                Uri data = intent.getData();
                String pkg = (data != null) ? data.getSchemeSpecificPart() : null;
                Log.i(TAG, "Package broadcast received: " + action + " for " + pkg);

                // Evict cached icon so new/updated icons load immediately
                if (pkg != null) {
                    ICON_CACHE.remove(pkg);
                    if (Intent.ACTION_PACKAGE_REMOVED.equals(action)) {
                        SupportedAppsRegistry.unregisterAllowedPackage(pkg);
                        selectionManager.onPackageRemoved(pkg);
                    }
                }

                if (isCleaningInProgress) {
                    // Avoid disturbing an active cleaning batch; refresh once batch completes
                    needsRefreshAfterBatch = true;
                } else {
                    refreshAppInventory(false);
                }
            }
        };

        IntentFilter filter = new IntentFilter();
        filter.addAction(Intent.ACTION_PACKAGE_ADDED);
        filter.addAction(Intent.ACTION_PACKAGE_REMOVED);
        filter.addAction(Intent.ACTION_PACKAGE_REPLACED);
        filter.addAction(Intent.ACTION_PACKAGE_CHANGED);
        filter.addDataScheme("package");
        registerReceiver(packageChangeReceiver, filter);
        isReceiverRegistered = true;
    }

    private void unregisterPackageReceiver() {
        if (isReceiverRegistered && packageChangeReceiver != null) {
            try {
                unregisterReceiver(packageChangeReceiver);
            } catch (Exception ignored) {}
            isReceiverRegistered = false;
        }
    }

    // =========================================================================
    // PilotController.PilotListener Callbacks
    // =========================================================================

    @Override
    public void onPilotStateChanged(final PilotController.State state, final String message) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (isCleaningInProgress && tvBatchProgressStep != null) {
                    tvBatchProgressStep.setText(message);
                }
            }
        });
    }

    @Override
    public void onPilotFinished(PilotController.PilotResult result) {
        // Individual pilot finished; batch listener aggregates results
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
                        "Processing %d of %d: %s", currentIndex + 1, total, currentApp.getResolvedLabel()));
                tvBatchProgressStep.setText("Opening App Info & verifying identity...");
                adapter.updateApp(currentApp);
            }
        });
    }

    @Override
    public void onAppFinished(final TargetApp app, PilotController.Outcome outcome) {
        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                adapter.updateApp(app);
            }
        });
    }

    @Override
    public void onBatchFinished(final List<TargetApp> batchResults, final String summary) {
        final long batchDurationMs = System.currentTimeMillis() - batchStartTime;
        final long batchStorageAfter = StorageMeasurementUtil.getAvailableStorageBytes();

        int cleared = 0, alreadyClean = 0, failed = 0, cancelled = 0;
        if (batchResults != null) {
            for (TargetApp app : batchResults) {
                if (app.getStatus() == TargetApp.Status.CLEARED) cleared++;
                else if (app.getStatus() == TargetApp.Status.ALREADY_CLEAN) alreadyClean++;
                else if (app.getStatus() == TargetApp.Status.FAILED) failed++;
                else if (app.getStatus() == TargetApp.Status.CANCELLED || app.getStatus() == TargetApp.Status.SKIPPED) cancelled++;
            }
        }

        final CleaningHistoryRecord.Outcome outcome =
                CleaningHistoryRecord.evaluateBatchOutcome(summary, cleared, alreadyClean, failed, cancelled);

        final StorageMeasurementUtil.StorageMeasurement measurement =
                StorageMeasurementUtil.calculateMeasurement(batchStorageBefore, batchStorageAfter);

        final String honestSummary;
        if (measurement.isValid) {
            honestSummary = summary + "\n\n" + measurement.getDescription();
        } else {
            honestSummary = summary;
        }

        CleaningHistoryRecord record = CleaningHistoryRecord.createDeepClean(
                outcome,
                batchDurationMs,
                batchStorageBefore,
                batchStorageAfter,
                (batchResults != null ? batchResults.size() : 0),
                cleared,
                alreadyClean,
                failed,
                cancelled,
                summary
        );
        CleaningHistoryRepository.getInstance(AppSelectionActivity.this).addRecord(record);

        runOnUiThread(new Runnable() {
            @Override
            public void run() {
                isCleaningInProgress = false;
                setSelectionControlsEnabled(true);
                btnCancelBatch.setVisibility(View.GONE);
                llBatchProgress.setVisibility(View.GONE);
                updateSelectionSummary();

                if (batchResults != null) {
                    for (TargetApp app : batchResults) {
                        adapter.updateApp(app);
                    }
                }

                llBatchSummary.setVisibility(View.VISIBLE);

                if (outcome == CleaningHistoryRecord.Outcome.CANCELLED) {
                    tvBatchSummaryTitle.setText("BATCH CANCELLED");
                    tvBatchSummaryTitle.setTextColor(getColor(android.R.color.holo_orange_light));
                } else if (outcome == CleaningHistoryRecord.Outcome.FAILED) {
                    tvBatchSummaryTitle.setText("BATCH FAILED");
                    tvBatchSummaryTitle.setTextColor(getColor(android.R.color.holo_red_light));
                } else if (outcome == CleaningHistoryRecord.Outcome.PARTIAL) {
                    tvBatchSummaryTitle.setText("BATCH STOPPED (PARTIAL)");
                    tvBatchSummaryTitle.setTextColor(getColor(android.R.color.holo_red_light));
                } else {
                    tvBatchSummaryTitle.setText("BATCH RESULTS SUMMARY");
                    tvBatchSummaryTitle.setTextColor(getColor(R.color.accent_badge));
                }

                tvBatchSummaryText.setText(honestSummary);

                if (needsRefreshAfterBatch) {
                    needsRefreshAfterBatch = false;
                    refreshAppInventory(false);
                }
            }
        });
    }

    @Override
    protected void onResume() {
        super.onResume();
        updateAccessibilityStatusBanner();

        if (!isCleaningInProgress) {
            // Automatically detect any installs/uninstalls that happened while in background
            refreshAppInventory(false);
        }
    }

    // =========================================================================
    // RecyclerView Adapter
    // =========================================================================

    private class TargetAppAdapter extends RecyclerView.Adapter<TargetAppAdapter.ViewHolder> {
        private List<TargetApp> apps = new ArrayList<>();
        private boolean isSelectable = true;

        public void setApps(List<TargetApp> newApps) {
            this.apps = (newApps != null) ? newApps : Collections.<TargetApp>emptyList();
            notifyDataSetChanged();
        }

        public void setSelectable(boolean selectable) {
            this.isSelectable = selectable;
            notifyDataSetChanged();
        }

        public void updateApp(TargetApp app) {
            if (app == null) return;
            for (int i = 0; i < apps.size(); i++) {
                if (apps.get(i).getPackageName().equals(app.getPackageName())) {
                    notifyItemChanged(i);
                    break;
                }
            }
        }

        @NonNull
        @Override
        public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
            View view = LayoutInflater.from(parent.getContext()).inflate(R.layout.item_target_app, parent, false);
            return new ViewHolder(view);
        }

        @Override
        public void onBindViewHolder(@NonNull final ViewHolder holder, int position) {
            // 1. Immediately detach listeners to prevent recycled view callbacks from mutating selection state
            holder.cbSelect.setOnCheckedChangeListener(null);
            holder.cbSelect.setOnClickListener(null);
            holder.itemView.setOnClickListener(null);

            final TargetApp app = apps.get(position);
            final String pkg = app.getPackageName();
            final boolean isProtected = app.isProtected();
            final boolean isSelected = selectionManager.isSelected(pkg, isProtected);

            // Synchronize model state with authoritative selection
            app.setSelected(isSelected);

            holder.tvLabel.setText(app.getResolvedLabel());
            holder.tvPackage.setText(pkg);

            if (holder.tvClassification != null) {
                TargetApp.Classification cl = app.getClassification();
                holder.tvClassification.setText(cl.getDisplayName());
                if (cl == TargetApp.Classification.USER_APP) {
                    holder.tvClassification.setTextColor(getColor(R.color.badge_user_app));
                } else if (cl == TargetApp.Classification.PREINSTALLED_APP) {
                    holder.tvClassification.setTextColor(getColor(R.color.badge_preinstalled));
                } else {
                    holder.tvClassification.setTextColor(getColor(R.color.badge_protected));
                }
            }

            if (isProtected) {
                holder.tvDetail.setText(app.getStatusDetail());
                holder.cbSelect.setEnabled(false);
                holder.cbSelect.setChecked(false);
                holder.itemView.setAlpha(0.65f);
            } else {
                if (app.getStatus() == TargetApp.Status.PENDING && (app.getCacheBefore() == null || app.getCacheBefore().isEmpty())) {
                    holder.tvDetail.setText("Cache: Measured live during cleaning");
                } else {
                    holder.tvDetail.setText(app.getStatusDetail());
                }
                holder.cbSelect.setEnabled(isSelectable && !isCleaningInProgress);
                holder.cbSelect.setChecked(isSelected);
                holder.itemView.setAlpha(1.0f);
            }

            // Asynchronous icon loading with cache
            Drawable cachedIcon = ICON_CACHE.get(pkg);
            if (cachedIcon != null) {
                holder.ivIcon.setImageDrawable(cachedIcon);
            } else {
                holder.ivIcon.setImageResource(R.drawable.ic_broom);
                holder.ivIcon.setTag(pkg);
                final PackageManager pm = getPackageManager();
                ICON_LOADER_EXECUTOR.execute(new Runnable() {
                    @Override
                    public void run() {
                        try {
                            final Drawable icon = pm.getApplicationIcon(pkg);
                            if (icon != null) {
                                ICON_CACHE.put(pkg, icon);
                                runOnUiThread(new Runnable() {
                                    @Override
                                    public void run() {
                                        if (pkg.equals(holder.ivIcon.getTag())) {
                                            holder.ivIcon.setImageDrawable(icon);
                                        }
                                    }
                                });
                            }
                        } catch (Throwable ignored) {}
                    }
                });
            }

            updateBadgeAppearance(holder.tvStatusBadge, app.getStatus());

            // User-initiated click listener for checkbox and item row
            View.OnClickListener clickListener = new View.OnClickListener() {
                @Override
                public void onClick(View v) {
                    if (isCleaningInProgress || !isSelectable) {
                        holder.cbSelect.setChecked(selectionManager.isSelected(pkg, isProtected));
                        return;
                    }
                    if (isProtected) {
                        holder.cbSelect.setChecked(false);
                        Toast.makeText(AppSelectionActivity.this, app.getStatusDetail(), Toast.LENGTH_SHORT).show();
                        return;
                    }

                    boolean nextState;
                    if (v == holder.cbSelect) {
                        nextState = holder.cbSelect.isChecked();
                    } else {
                        nextState = !selectionManager.isSelected(pkg, isProtected);
                        holder.cbSelect.setChecked(nextState);
                    }

                    selectionManager.setSelected(pkg, nextState, isProtected);
                    app.setSelected(nextState);
                    updateSelectionSummary();
                }
            };

            holder.cbSelect.setOnClickListener(clickListener);
            holder.itemView.setOnClickListener(clickListener);
        }

        @Override
        public void onViewRecycled(@NonNull ViewHolder holder) {
            super.onViewRecycled(holder);
            holder.cbSelect.setOnCheckedChangeListener(null);
            holder.cbSelect.setOnClickListener(null);
            holder.itemView.setOnClickListener(null);
        }

        @Override
        public int getItemCount() {
            return apps.size();
        }

        class ViewHolder extends RecyclerView.ViewHolder {
            CheckBox cbSelect;
            ImageView ivIcon;
            TextView tvLabel;
            TextView tvPackage;
            TextView tvClassification;
            TextView tvDetail;
            TextView tvStatusBadge;

            ViewHolder(@NonNull View itemView) {
                super(itemView);
                cbSelect = itemView.findViewById(R.id.cb_app_select);
                ivIcon = itemView.findViewById(R.id.iv_app_icon);
                tvLabel = itemView.findViewById(R.id.tv_app_label);
                tvPackage = itemView.findViewById(R.id.tv_app_package);
                tvClassification = itemView.findViewById(R.id.tv_app_classification);
                tvDetail = itemView.findViewById(R.id.tv_app_detail);
                tvStatusBadge = itemView.findViewById(R.id.tv_app_status_badge);
            }
        }
    }
}
