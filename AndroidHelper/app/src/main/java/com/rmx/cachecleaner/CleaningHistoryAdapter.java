package com.rmx.cachecleaner;

import android.content.Context;
import android.view.LayoutInflater;
import android.view.View;
import android.view.ViewGroup;
import android.widget.TextView;

import androidx.annotation.NonNull;
import androidx.core.content.ContextCompat;
import androidx.recyclerview.widget.RecyclerView;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Adapter for rendering cleaning history entries in RMX Device Hub.
 */
public class CleaningHistoryAdapter extends RecyclerView.Adapter<CleaningHistoryAdapter.ViewHolder> {

    private final List<CleaningHistoryRecord> records = new ArrayList<>();

    public void setRecords(List<CleaningHistoryRecord> newRecords) {
        records.clear();
        if (newRecords != null) {
            records.addAll(newRecords);
        }
        notifyDataSetChanged();
    }

    @NonNull
    @Override
    public ViewHolder onCreateViewHolder(@NonNull ViewGroup parent, int viewType) {
        View view = LayoutInflater.from(parent.getContext())
                .inflate(R.layout.item_cleaning_history, parent, false);
        return new ViewHolder(view);
    }

    @Override
    public void onBindViewHolder(@NonNull ViewHolder holder, int position) {
        CleaningHistoryRecord record = records.get(position);
        Context context = holder.itemView.getContext();

        // 1. Clean Type Badge
        CleaningHistoryRecord.CleanType type = record.getCleanType();
        if (type == CleaningHistoryRecord.CleanType.QUICK_CLEAN) {
            holder.tvTypeBadge.setText("QUICK CLEAN");
            holder.tvTypeBadge.setTextColor(ContextCompat.getColor(context, R.color.accent_primary));
        } else if (type == CleaningHistoryRecord.CleanType.DEEP_CLEAN) {
            holder.tvTypeBadge.setText("GUARDED DEEP CLEAN");
            holder.tvTypeBadge.setTextColor(ContextCompat.getColor(context, R.color.badge_user_app));
        } else {
            holder.tvTypeBadge.setText("TEST PILOT");
            holder.tvTypeBadge.setTextColor(ContextCompat.getColor(context, R.color.badge_preinstalled));
        }

        // 2. Outcome Badge
        CleaningHistoryRecord.Outcome outcome = record.getOutcome();
        holder.tvOutcomeBadge.setText(outcome.getDisplayName());
        switch (outcome) {
            case SUCCESS:
                holder.tvOutcomeBadge.setTextColor(ContextCompat.getColor(context, R.color.status_success));
                break;
            case PARTIAL:
            case CANCELLED:
                holder.tvOutcomeBadge.setTextColor(ContextCompat.getColor(context, R.color.status_warning));
                break;
            case FAILED:
            default:
                holder.tvOutcomeBadge.setTextColor(ContextCompat.getColor(context, R.color.status_error));
                break;
        }

        // 3. Timestamp
        holder.tvTimestamp.setText(record.getFormattedDate());

        // 4. Storage Delta & Label
        if (!record.isStorageMeasured()) {
            holder.tvStorageDelta.setText("Unavailable");
            holder.tvStorageDelta.setTextColor(ContextCompat.getColor(context, R.color.text_muted));
            holder.tvDeltaLabel.setText("storage measurement unavailable");
            holder.tvStorageReadings.setText("Free storage before and after could not be measured");
        } else {
            long delta = record.getBytesDelta();
            if (delta > 0) {
                holder.tvStorageDelta.setText("+" + StorageMeasurementUtil.formatBytes(delta));
                holder.tvStorageDelta.setTextColor(ContextCompat.getColor(context, R.color.status_success));
                holder.tvDeltaLabel.setText("observed storage reclaimed");
            } else if (delta == 0) {
                holder.tvStorageDelta.setText("0 B");
                holder.tvStorageDelta.setTextColor(ContextCompat.getColor(context, R.color.text_secondary));
                holder.tvDeltaLabel.setText("no storage change observed");
            } else {
                holder.tvStorageDelta.setText("-" + StorageMeasurementUtil.formatBytes(Math.abs(delta)));
                holder.tvStorageDelta.setTextColor(ContextCompat.getColor(context, R.color.status_warning));
                holder.tvDeltaLabel.setText("decreased due to background activity");
            }

            holder.tvStorageReadings.setText(String.format(Locale.US,
                    "Free storage: %s → %s",
                    StorageMeasurementUtil.formatBytes(record.getFreeBytesBefore()),
                    StorageMeasurementUtil.formatBytes(record.getFreeBytesAfter())));
        }

        // 5. Duration
        holder.tvDuration.setText("Duration: " + record.getFormattedDuration());

        // 6. Apps Breakdown
        if (type == CleaningHistoryRecord.CleanType.DEEP_CLEAN && record.getAppsTargeted() > 0) {
            holder.tvAppBreakdown.setVisibility(View.VISIBLE);
            holder.tvAppBreakdown.setText(String.format(Locale.US,
                    "Apps: %d cleared, %d clean, %d failed (%d total)",
                    record.getAppsCleared(), record.getAppsAlreadyClean(),
                    record.getAppsFailed(), record.getAppsTargeted()));
        } else {
            holder.tvAppBreakdown.setVisibility(View.GONE);
        }

        // 7. Summary / Note
        String summary = record.getSummaryMessage();
        if (summary != null && !summary.isEmpty()) {
            holder.tvSummary.setText(summary);
            holder.tvSummary.setVisibility(View.VISIBLE);
        } else if (!record.getDetailNote().isEmpty()) {
            holder.tvSummary.setText(record.getDetailNote());
            holder.tvSummary.setVisibility(View.VISIBLE);
        } else {
            holder.tvSummary.setVisibility(View.GONE);
        }
    }

    @Override
    public int getItemCount() {
        return records.size();
    }

    static class ViewHolder extends RecyclerView.ViewHolder {
        final TextView tvTypeBadge;
        final TextView tvOutcomeBadge;
        final TextView tvTimestamp;
        final TextView tvStorageDelta;
        final TextView tvDeltaLabel;
        final TextView tvStorageReadings;
        final TextView tvDuration;
        final TextView tvAppBreakdown;
        final TextView tvSummary;

        ViewHolder(@NonNull View itemView) {
            super(itemView);
            tvTypeBadge = itemView.findViewById(R.id.tv_item_type_badge);
            tvOutcomeBadge = itemView.findViewById(R.id.tv_item_outcome_badge);
            tvTimestamp = itemView.findViewById(R.id.tv_item_timestamp);
            tvStorageDelta = itemView.findViewById(R.id.tv_item_storage_delta);
            tvDeltaLabel = itemView.findViewById(R.id.tv_item_delta_label);
            tvStorageReadings = itemView.findViewById(R.id.tv_item_storage_readings);
            tvDuration = itemView.findViewById(R.id.tv_item_duration);
            tvAppBreakdown = itemView.findViewById(R.id.tv_item_app_breakdown);
            tvSummary = itemView.findViewById(R.id.tv_item_summary);
        }
    }
}
