package com.rmx.cachecleaner;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.DialogInterface;
import android.os.Bundle;
import android.view.View;
import android.widget.ImageButton;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import androidx.recyclerview.widget.LinearLayoutManager;
import androidx.recyclerview.widget.RecyclerView;

import java.util.List;
import java.util.Locale;

/**
 * Audit and History Screen for RMX Device Hub.
 *
 * Displays up to 50 most recent cleaning operations (newest first)
 * with measured storage deltas, execution duration, and outcome details.
 */
public class CleaningHistoryActivity extends Activity {

    private ImageButton btnBack;
    private ImageButton btnClearHistory;
    private TextView tvHistoryCount;
    private RecyclerView rvHistoryList;
    private LinearLayout llHistoryEmpty;

    private CleaningHistoryAdapter adapter;
    private CleaningHistoryRepository repository;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        setContentView(R.layout.activity_cleaning_history);

        repository = CleaningHistoryRepository.getInstance(this);

        initViews();
        setupRecyclerView();
        setupListeners();
        loadHistory();
    }

    private void initViews() {
        btnBack = findViewById(R.id.btn_back);
        btnClearHistory = findViewById(R.id.btn_clear_history);
        tvHistoryCount = findViewById(R.id.tv_history_count);
        rvHistoryList = findViewById(R.id.rv_history_list);
        llHistoryEmpty = findViewById(R.id.ll_history_empty);
    }

    private void setupRecyclerView() {
        adapter = new CleaningHistoryAdapter();
        rvHistoryList.setLayoutManager(new LinearLayoutManager(this));
        rvHistoryList.setAdapter(adapter);
    }

    private void setupListeners() {
        btnBack.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                finish();
            }
        });

        btnClearHistory.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                confirmClearHistory();
            }
        });
    }

    private void loadHistory() {
        List<CleaningHistoryRecord> records = repository.getRecords();
        int count = records.size();

        tvHistoryCount.setText(String.format(Locale.US,
                "RECORDED OPERATIONS (%d OF %d)", count, CleaningHistoryRepository.MAX_ENTRIES));

        if (count == 0) {
            rvHistoryList.setVisibility(View.GONE);
            llHistoryEmpty.setVisibility(View.VISIBLE);
            btnClearHistory.setVisibility(View.GONE);
        } else {
            rvHistoryList.setVisibility(View.VISIBLE);
            llHistoryEmpty.setVisibility(View.GONE);
            btnClearHistory.setVisibility(View.VISIBLE);
            adapter.setRecords(records);
        }
    }

    private void confirmClearHistory() {
        int count = repository.getRecordCount();
        if (count == 0) return;

        new AlertDialog.Builder(this)
                .setTitle("Clear Cleaning History?")
                .setMessage(String.format(Locale.US,
                        "This will permanently remove all %d recorded cleaning operations from local storage.\n\nThis action cannot be undone.",
                        count))
                .setPositiveButton("CLEAR ALL", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        repository.clearHistory();
                        loadHistory();
                        Toast.makeText(CleaningHistoryActivity.this, "Cleaning history cleared.", Toast.LENGTH_SHORT).show();
                    }
                })
                .setNegativeButton(android.R.string.cancel, null)
                .show();
    }
}
