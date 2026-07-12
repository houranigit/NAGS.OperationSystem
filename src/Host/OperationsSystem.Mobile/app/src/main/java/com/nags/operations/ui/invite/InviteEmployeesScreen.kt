package com.nags.operations.ui.invite

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.CloudOff
import androidx.compose.material.icons.filled.GroupAdd
import androidx.compose.material.icons.filled.Person
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.ui.util.formatIsoForDisplay

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun InviteEmployeesScreen(
    viewModel: InviteEmployeesViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val appCtx = LocalContext.current.applicationContext

    BackHandler(onBack = onBack)

    LaunchedEffect(state.inviteSucceeded) {
        if (state.inviteSucceeded) {
            Toast.makeText(appCtx, "Invitations sent", Toast.LENGTH_SHORT).show()
            onBack()
        }
    }

    LaunchedEffect(state.error) {
        state.error?.let {
            Toast.makeText(appCtx, it, Toast.LENGTH_LONG).show()
            viewModel.clearError()
        }
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Invite teammates", fontWeight = FontWeight.SemiBold) },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
        bottomBar = {
            InviteBottomBar(
                isOnline = state.isOnline,
                selectedCount = state.selectedIds.size,
                isSubmitting = state.isSubmitting,
                onInvite = viewModel::invite,
            )
        },
    ) { padding ->
        when {
            state.isLoading -> Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentAlignment = Alignment.Center,
            ) {
                CircularProgressIndicator()
            }

            else -> LazyColumn(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentPadding = PaddingValues(16.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                item {
                    FlightHeader(
                        flightNumber = state.flightNumber,
                        customerIataCode = state.customerIataCode,
                        stationCode = state.stationCode,
                        sta = state.sta,
                    )
                }

                if (!state.isOnline) {
                    item { OfflineNotice() }
                }

                item {
                    SectionTitle("Assigned to this flight (${state.assigned.size})")
                }
                if (state.assigned.isEmpty()) {
                    item {
                        Text(
                            "No one is assigned yet.",
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                } else {
                    items(state.assigned, key = { "assigned_${it.employeeId}" }) { row ->
                        AssignedEmployeeRow(name = row.fullName, role = row.employeeNumber)
                    }
                }

                item {
                    SectionTitle("Other employees at ${state.stationCode.ifBlank { "this station" }}")
                }
                item {
                    OutlinedTextField(
                        value = state.search,
                        onValueChange = viewModel::setSearch,
                        placeholder = { Text("Search employees..") },
                        leadingIcon = { Icon(Icons.Default.Search, contentDescription = null) },
                        singleLine = true,
                        shape = RoundedCornerShape(14.dp),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                if (state.candidates.isEmpty()) {
                    item {
                        Text(
                            if (state.search.isBlank()) {
                                "No other employees at this station."
                            } else {
                                "No employees match \"${state.search}\"."
                            },
                            style = MaterialTheme.typography.bodyMedium,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                } else {
                    items(state.candidates, key = { "cand_${it.employeeId}" }) { row ->
                        CandidateEmployeeRow(
                            name = row.fullName,
                            role = row.employeeNumber,
                            selected = row.employeeId in state.selectedIds,
                            enabled = state.isOnline && !state.isSubmitting,
                            onToggle = { viewModel.toggleSelection(row.employeeId) },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun FlightHeader(
    flightNumber: String,
    customerIataCode: String,
    stationCode: String,
    sta: String,
) {
    Surface(
        color = MaterialTheme.colorScheme.surfaceVariant,
        shape = RoundedCornerShape(14.dp),
        modifier = Modifier.fillMaxWidth(),
    ) {
        Column(
            modifier = Modifier.padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(4.dp),
        ) {
            Text(
                listOf(customerIataCode, flightNumber).filter { it.isNotBlank() }.joinToString(" "),
                style = MaterialTheme.typography.titleMedium,
                fontWeight = FontWeight.SemiBold,
            )
            Text(
                "Station $stationCode · STA ${formatIsoForDisplay(sta)}",
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }
}

@Composable
private fun OfflineNotice() {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier
            .fillMaxWidth()
            .background(
                MaterialTheme.colorScheme.errorContainer,
                RoundedCornerShape(12.dp),
            )
            .padding(12.dp),
    ) {
        Icon(
            Icons.Default.CloudOff,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.onErrorContainer,
            modifier = Modifier.size(20.dp),
        )
        Text(
            "You're offline. You can review the roster, but inviting needs a connection.",
            style = MaterialTheme.typography.bodySmall,
            color = MaterialTheme.colorScheme.onErrorContainer,
        )
    }
}

@Composable
private fun SectionTitle(text: String) {
    Text(
        text,
        style = MaterialTheme.typography.titleSmall,
        fontWeight = FontWeight.SemiBold,
        modifier = Modifier.padding(top = 6.dp),
    )
}

@Composable
private fun AssignedEmployeeRow(name: String, role: String) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(12.dp),
        modifier = Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(12.dp))
            .padding(horizontal = 14.dp, vertical = 12.dp),
    ) {
        Icon(
            Icons.Default.Person,
            contentDescription = null,
            tint = MaterialTheme.colorScheme.primary,
            modifier = Modifier.size(22.dp),
        )
        Column(Modifier.weight(1f)) {
            Text(name, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
            if (role.isNotBlank()) {
                Text(
                    role,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun CandidateEmployeeRow(
    name: String,
    role: String,
    selected: Boolean,
    enabled: Boolean,
    onToggle: () -> Unit,
) {
    val container =
        if (selected) MaterialTheme.colorScheme.primaryContainer else MaterialTheme.colorScheme.surface
    Row(
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(8.dp),
        modifier = Modifier
            .fillMaxWidth()
            .background(container, RoundedCornerShape(12.dp))
            .clickable(enabled = enabled, onClick = onToggle)
            .padding(horizontal = 12.dp, vertical = 8.dp),
    ) {
        Checkbox(checked = selected, onCheckedChange = { onToggle() }, enabled = enabled)
        Column(Modifier.weight(1f)) {
            Text(name, style = MaterialTheme.typography.bodyLarge, fontWeight = FontWeight.Medium)
            if (role.isNotBlank()) {
                Text(
                    role,
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }
    }
}

@Composable
private fun InviteBottomBar(
    isOnline: Boolean,
    selectedCount: Int,
    isSubmitting: Boolean,
    onInvite: () -> Unit,
) {
    Surface(tonalElevation = 3.dp) {
        Box(Modifier.padding(16.dp)) {
            Button(
                onClick = onInvite,
                enabled = isOnline && selectedCount > 0 && !isSubmitting,
                modifier = Modifier
                    .fillMaxWidth()
                    .height(54.dp),
                shape = RoundedCornerShape(14.dp),
            ) {
                if (isSubmitting) {
                    CircularProgressIndicator(
                        modifier = Modifier.size(22.dp),
                        strokeWidth = 2.dp,
                        color = MaterialTheme.colorScheme.onPrimary,
                    )
                } else {
                    Row(
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(10.dp),
                    ) {
                        Icon(
                            if (isOnline) Icons.Default.GroupAdd else Icons.Default.CloudOff,
                            contentDescription = null,
                        )
                        Text(
                            when {
                                !isOnline -> "Offline"
                                selectedCount > 0 -> "Invite ($selectedCount)"
                                else -> "Invite"
                            },
                            fontWeight = FontWeight.SemiBold,
                        )
                    }
                }
            }
        }
    }
}
