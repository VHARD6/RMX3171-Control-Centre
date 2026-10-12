package com.rmx.cachecleaner;

import java.util.Collection;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * Authoritative selection state manager for RMX Cache Cleaner.
 * 
 * Enforces:
 * 1. Single source of truth: selectedPackages is the definitive selection set.
 * 2. Protection guard: protected components can NEVER be selected.
 * 3. Recycling safety: view recycling, scrolling, rebinding and filtering NEVER mutate selection.
 * 4. User interaction isolation: only explicit user actions modify selection state.
 */
public class AppSelectionManager {

    private final Set<String> selectedPackages = new HashSet<>();
    private boolean hasInitialized = false;

    public synchronized boolean isInitialized() {
        return hasInitialized;
    }

    public synchronized void setInitialized(boolean initialized) {
        this.hasInitialized = initialized;
    }

    public synchronized Set<String> getSelectedPackages() {
        return Collections.unmodifiableSet(new HashSet<>(selectedPackages));
    }

    public synchronized int getSelectedCount() {
        return selectedPackages.size();
    }

    public synchronized boolean isSelected(String packageName, boolean isProtected) {
        if (isProtected || packageName == null) {
            return false;
        }
        return selectedPackages.contains(packageName);
    }

    public synchronized boolean setSelected(String packageName, boolean selected, boolean isProtected) {
        if (packageName == null || isProtected) {
            if (packageName != null) {
                selectedPackages.remove(packageName);
            }
            return false;
        }
        if (selected) {
            selectedPackages.add(packageName);
        } else {
            selectedPackages.remove(packageName);
        }
        return selected;
    }

    public synchronized boolean toggle(String packageName, boolean isProtected) {
        if (packageName == null || isProtected) {
            if (packageName != null) {
                selectedPackages.remove(packageName);
            }
            return false;
        }
        boolean newState = !selectedPackages.contains(packageName);
        if (newState) {
            selectedPackages.add(packageName);
        } else {
            selectedPackages.remove(packageName);
        }
        return newState;
    }

    public synchronized void selectAll(Collection<TargetApp> apps) {
        if (apps == null) return;
        for (TargetApp app : apps) {
            if (app != null && !app.isProtected()) {
                selectedPackages.add(app.getPackageName());
                app.setSelected(true);
            }
        }
    }

    public synchronized void deselectAll(Collection<TargetApp> apps) {
        if (apps == null) return;
        for (TargetApp app : apps) {
            if (app != null) {
                selectedPackages.remove(app.getPackageName());
                app.setSelected(false);
            }
        }
    }

    public synchronized void invertSelection(Collection<TargetApp> apps) {
        if (apps == null) return;
        for (TargetApp app : apps) {
            if (app != null && !app.isProtected()) {
                String pkg = app.getPackageName();
                boolean newState = !selectedPackages.contains(pkg);
                if (newState) {
                    selectedPackages.add(pkg);
                } else {
                    selectedPackages.remove(pkg);
                }
                app.setSelected(newState);
            }
        }
    }

    public synchronized void onPackageRemoved(String packageName) {
        if (packageName != null) {
            selectedPackages.remove(packageName);
        }
    }

    public synchronized void retainOnly(Collection<String> installedPackages) {
        if (installedPackages != null) {
            selectedPackages.retainAll(installedPackages);
        }
    }

    public synchronized void clear() {
        selectedPackages.clear();
        hasInitialized = false;
    }

    /**
     * Initializes selection on first run: selects all non-protected apps.
     */
    public synchronized void initializeDefaultSelection(List<TargetApp> apps) {
        selectedPackages.clear();
        if (apps != null) {
            for (TargetApp app : apps) {
                if (app != null && !app.isProtected()) {
                    selectedPackages.add(app.getPackageName());
                    app.setSelected(true);
                } else if (app != null) {
                    app.setSelected(false);
                }
            }
        }
        hasInitialized = true;
    }

    /**
     * Synchronizes a list of TargetApp objects so each TargetApp's isSelected()
     * matches the authoritative selection state.
     */
    public synchronized void syncAppSelectionState(Collection<TargetApp> apps) {
        if (apps == null) return;
        for (TargetApp app : apps) {
            if (app != null) {
                boolean selected = isSelected(app.getPackageName(), app.isProtected());
                app.setSelected(selected);
            }
        }
    }

    /**
     * Counts how many non-protected apps in the provided collection are currently selected.
     */
    public synchronized int countSelectedIn(Collection<TargetApp> apps) {
        if (apps == null) return 0;
        int count = 0;
        for (TargetApp app : apps) {
            if (app != null && !app.isProtected() && selectedPackages.contains(app.getPackageName())) {
                count++;
            }
        }
        return count;
    }
}
