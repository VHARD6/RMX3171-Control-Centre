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
        FAILED
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

    public TargetApp(String packageName, String defaultLabel) {
        this.packageName = packageName;
        this.defaultLabel = defaultLabel;
        this.resolvedLabel = defaultLabel;
        this.isInstalled = false;
        this.isSelected = true; // selected by default if installed
        this.status = Status.PENDING;
        this.statusDetail = "Pending";
        this.cacheBefore = "";
        this.cacheAfter = "";
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
        isSelected = selected;
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
        this.status = Status.PENDING;
        this.statusDetail = "Pending";
        this.cacheBefore = "";
        this.cacheAfter = "";
    }
}
