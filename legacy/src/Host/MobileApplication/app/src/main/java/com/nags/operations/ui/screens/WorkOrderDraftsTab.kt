package com.nags.operations.ui.screens

import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.IntrinsicSize
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.Note
import androidx.compose.material.icons.filled.DeleteOutline
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.IconButton
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.data.db.entities.WorkOrderDraftEntity
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.util.formatIsoForDisplay
import com.nags.operations.ui.workorder.WorkOrderDraftsViewModel

@Composable
fun WorkOrderDraftsTab(
    viewModel: WorkOrderDraftsViewModel,
    onOpenDraft: (draftId: String) -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    var draftPendingDelete by remember { mutableStateOf<WorkOrderDraftEntity?>(null) }

    draftPendingDelete?.let { draft ->
        AlertDialog(
            onDismissRequest = { draftPendingDelete = null },
            title = { Text("Delete draft?") },
            text = {
                Text(
                    "Remove the saved draft for ${draft.flightNumber} (${draft.customerName})? " +
                        "This cannot be undone.",
                )
            },
            confirmButton = {
                TextButton(
                    onClick = {
                        viewModel.deleteDraft(draft.draftId)
                        draftPendingDelete = null
                    },
                ) {
                    Text("Delete", color = MaterialTheme.colorScheme.error)
                }
            },
            dismissButton = {
                TextButton(onClick = { draftPendingDelete = null }) {
                    Text("Cancel")
                }
            },
        )
    }

    Column(modifier = Modifier.fillMaxSize()) {
        OutlinedTextField(
            value = state.search,
            onValueChange = viewModel::setSearch,
            placeholder = { Text("Search drafts by flight, customer, station…") },
            leadingIcon = { Icon(Icons.Filled.Search, contentDescription = null) },
            singleLine = true,
            shape = RoundedCornerShape(14.dp),
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = 16.dp, vertical = 8.dp),
        )

        when {
            state.filteredDrafts.isEmpty() && state.allDrafts.isEmpty() -> Box(
                modifier = Modifier.fillMaxSize(),
                contentAlignment = Alignment.Center,
            ) {
                EmptyState(
                    icon = Icons.AutoMirrored.Filled.Note,
                    title = "No drafts yet",
                    message = "Save a work order as draft from the create screen. It will appear here for you to finish later.",
                )
            }

            state.filteredDrafts.isEmpty() -> Box(
                modifier = Modifier.fillMaxSize(),
                contentAlignment = Alignment.Center,
            ) {
                EmptyState(
                    icon = Icons.AutoMirrored.Filled.Note,
                    title = "No matching drafts",
                    message = "Nothing matches \"${state.search.trim()}\". Try a different search.",
                )
            }

            else -> LazyColumn(
                contentPadding = PaddingValues(horizontal = 16.dp, vertical = 8.dp),
                verticalArrangement = Arrangement.spacedBy(12.dp),
                modifier = Modifier.fillMaxSize(),
            ) {
                items(state.filteredDrafts, key = { it.draftId }) { draft ->
                    WorkOrderDraftListCard(
                        draft = draft,
                        onClick = { onOpenDraft(draft.draftId) },
                        onDeleteRequest = { draftPendingDelete = draft },
                    )
                }
            }
        }
    }
}

@Composable
private fun WorkOrderDraftListCard(
    draft: WorkOrderDraftEntity,
    onClick: () -> Unit,
    onDeleteRequest: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val stripe = MaterialTheme.colorScheme.tertiary
    Card(
        modifier = modifier.fillMaxWidth(),
        shape = RoundedCornerShape(18.dp),
        elevation = CardDefaults.cardElevation(defaultElevation = 1.dp),
        colors = CardDefaults.cardColors(containerColor = MaterialTheme.colorScheme.surface),
    ) {
        Row(
            Modifier
                .fillMaxWidth()
                .height(IntrinsicSize.Min),
        ) {
            Box(
                Modifier
                    .width(6.dp)
                    .fillMaxHeight()
                    .background(stripe),
            )
            Column(
                Modifier
                    .weight(1f)
                    .clickable(onClick = onClick)
                    .padding(16.dp),
            ) {
                Text(
                    draft.flightNumber,
                    style = MaterialTheme.typography.titleMedium,
                    fontWeight = FontWeight.SemiBold,
                )
                Text(
                    draft.customerName,
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Row(
                    Modifier.padding(top = 6.dp),
                    horizontalArrangement = Arrangement.spacedBy(8.dp),
                ) {
                    Text(
                        draft.stationCode,
                        style = MaterialTheme.typography.labelMedium,
                        fontWeight = FontWeight.Medium,
                        color = MaterialTheme.colorScheme.primary,
                    )
                    Text(
                        "STA ${formatIsoForDisplay(draft.staIso)}",
                        style = MaterialTheme.typography.bodySmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                }
                Text(
                    "Draft · updated ${formatDraftUpdated(draft.updatedAtEpochMs)}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                    modifier = Modifier.padding(top = 4.dp),
                )
            }
            IconButton(
                onClick = onDeleteRequest,
                modifier = Modifier.align(Alignment.Top),
            ) {
                Icon(
                    Icons.Filled.DeleteOutline,
                    contentDescription = "Delete draft",
                    tint = MaterialTheme.colorScheme.error,
                )
            }
        }
    }
}

private fun formatDraftUpdated(epochMs: Long): String {
    // Short relative-free label for list rows; full date is in device locale.
    val sdf = java.text.SimpleDateFormat("MMM d, yyyy · HH:mm", java.util.Locale.getDefault())
    return sdf.format(java.util.Date(epochMs))
}
