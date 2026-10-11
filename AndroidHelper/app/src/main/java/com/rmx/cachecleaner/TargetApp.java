package com.rmx.cachecleaner;

import java.io.Serializable;

/**
 * Phase 5: Target App Model for Multi-App Cache Cleaning.
 * Represents an explicitly supported application target, its user selection state,
 * and its lifecycle status during sequential cleaning.
 */
public class TargetApp implements Serializable {

    public enum Status {
        PENDING,
        CLEANING,
        CLEARED,
        ALREADY_CLEAN,
        SKIPPED,
        FAILED,
        CANCELLED,
        PROTECTED,
        DISABLED
    }

    public enum Classification {
        USER_APP("User app"),
        PREINSTALLED_APP("Preinstalled app"),
        SYSTEM_COMPONENT("System component");

        private final String displayName;

        Classification(String displayName) {
            this.displayName = displayName;
        }

        public String getDisplayName() {
            return displayName;
        }
    }

    private final String packageName;
    private final String defaultLabel;
    private String resolvedLabel;
    private boolean isInstalled;
    private boolean isSelected;
    private Status status;
    private String statusDetail;
    private String cacheBefore;
    private String cacheAfter;
    private Classification classification;
    private boolean isProtected;
    private String protectionReason;
    private boolean isEnabled;

    public TargetApp(String packageName, String defaultLabel) {
        this.packageName = packageName;
        this.defaultLabel = defaultLabel;
        this.resolvedLabel = defaultLabel;
        this.isInstalled = false;
        this.isSelected = true; // selected by default if installed and selectable
        this.status = Status.PENDING;
        this.statusDetail = "Pending";
        this.cacheBefore = "";
        this.cacheAfter = "";
        this.classification = Classification.USER_APP;
        this.isProtected = false;
        this.protectionReason = "";
        this.isEnabled = true;
    }

    public String getPackageName() {
        return packageName;
    }

    public String getDefaultLabel() {
        return defaultLabel;
    }

    public String getResolvedLabel() {
        return (resolvedLabel != null && !resolvedLabel.isEmpty()) ? resolvedLabel : defaultLabel;
    }

    public void setResolvedLabel(String resolvedLabel) {
        this.resolvedLabel = resolvedLabel;
    }

    public boolean isInstalled() {
        return isInstalled;
    }

    public void setInstalled(boolean installed) {
        isInstalled = installed;
    }

    public boolean isSelected() {
        return isSelected;
    }

    public void setSelected(boolean selected) {
        if (isProtected) {
            isSelected = false;
            return;
        }
        isSelected = selected;
    }

    public Classification getClassification() {
        return classification != null ? classification : Classification.USER_APP;
    }

    public void setClassification(Classification classification) {
        this.classification = classification;
    }

    public boolean isProtected() {
        return isProtected;
    }

    public void setProtected(boolean isProtected) {
        this.isProtected = isProtected;
        if (isProtected) {
            this.isSelected = false;
        }
    }

    public String getProtectionReason() {
        return protectionReason != null ? protectionReason : "";
    }

    public void setProtectionReason(String protectionReason) {
        this.protectionReason = protectionReason;
    }

    public boolean isEnabled() {
        return isEnabled;
    }

    public void setEnabled(boolean isEnabled) {
        this.isEnabled = isEnabled;
    }

    public Status getStatus() {
        return status;
    }

    public void setStatus(Status status) {
        this.status = status;
    }

    public String getStatusDetail() {
        return statusDetail;
    }

    public void setStatusDetail(String statusDetail) {
        this.statusDetail = statusDetail;
    }

    public String getCacheBefore() {
        return cacheBefore;
    }

    public void setCacheBefore(String cacheBefore) {
        this.cacheBefore = cacheBefore;
    }

    public String getCacheAfter() {
        return cacheAfter;
    }

    public void setCacheAfter(String cacheAfter) {
        this.cacheAfter = cacheAfter;
    }

    public void resetForBatch() {
        this.status = isProtected ? Status.PROTECTED : Status.PENDING;
        this.statusDetail = isProtected ? ("Protected: " + getProtectionReason()) : "Pending";
        this.cacheBefore = "";
        this.cacheAfter = "";
    }
}
