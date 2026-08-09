package com.nags.operations.ui.screens

import android.widget.Toast
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.WorkOrderFlightSummaryCard
import com.nags.operations.ui.components.cleanupAllAttachmentPreviews
import com.nags.operations.ui.util.userTimeZone
import com.nags.operations.ui.workorder.ReturnToRampOccurrenceCard
import com.nags.operations.ui.workorder.ReturnToRampViewModel
import com.nags.operations.ui.workorder.SubmitOfflineResult
import com.nags.operations.ui.workorder.WorkOrderFlightLoadState

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReturnToRampScreen(
    viewModel: ReturnToRampViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val appContext = LocalContext.current.applicationContext

    DisposableEffect(appContext) {
        onDispose { cleanupAllAttachmentPreviews(appContext) }
    }

    BackHandler { if (!state.isSubmitting) onBack() }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Record return to ramp", fontWeight = FontWeight.SemiBold) },
                navigationIcon = {
                    IconButton(onClick = onBack, enabled = !state.isSubmitting) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        when (state.flightLoad) {
            WorkOrderFlightLoadState.Loading -> Box(
                modifier = Modifier.fillMaxSize().padding(padding),
                contentAlignment = Alignment.Center,
            ) { CircularProgressIndicator() }

            is WorkOrderFlightLoadState.Error -> Box(
                modifier = Modifier.fillMaxSize().padding(padding),
                contentAlignment = Alignment.Center,
            ) {
                ErrorState(
                    title = "Can't open return to ramp",
                    message = (state.flightLoad as WorkOrderFlightLoadState.Error).message,
                    onRetry = viewModel::retryLoadFlight,
                )
            }

            WorkOrderFlightLoadState.Ready -> {
                val flight = state.flight ?: return@Scaffold
                val occurrence = state.occurrence ?: return@Scaffold
                val localZone = remember { userTimeZone() }
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(padding)
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text(
                        "Record the occurrence window and the services or tasks completed during it. " +
                            "Each submission remains a separate event in the flight history.",
                        style = MaterialTheme.typography.bodyMedium,
                        color = MaterialTheme.colorScheme.onSurfaceVariant,
                    )
                    WorkOrderFlightSummaryCard(
                        customerName = flight.customerName,
                        customerIataCode = flight.customerIataCode.orEmpty(),
                        stationCode = flight.stationIata,
                        staIso = flight.sta,
                        stdIso = flight.std,
                        flightNumber = flight.flightNumber,
                        aircraftModel = flight.aircraftTypeModel,
                        operationTypeCode = flight.operationTypeName,
                    )
                    ReturnToRampOccurrenceCard(
                        occurrenceNumber = 1,
                        row = occurrence,
                        errors = state.submitErrors,
                        flightOffset = localZone,
                        scheduleAnchorIso = flight.sta,
                        services = state.catalogServices,
                        employees = state.catalogEmployees,
                        tools = state.catalogTools,
                        materials = state.catalogMaterials,
                        generalSupports = state.catalogGeneralSupports,
                        onChange = { updated -> viewModel.updateOccurrence { updated } },
                        onRemove = {},
                        canRemove = false,
                        onAddService = viewModel::addServiceLine,
                        onServiceChange = viewModel::replaceServiceLine,
                        onServiceRemove = viewModel::removeServiceLine,
                        onServiceAttachmentAdded = viewModel::addServiceLineAttachment,
                        onServiceAttachmentRemoved = viewModel::removeServiceLineAttachment,
                        onAddTask = viewModel::addTask,
                        onTaskChange = viewModel::replaceTask,
                        onTaskRemove = viewModel::removeTask,
                        onTaskAttachmentAdded = viewModel::addTaskAttachment,
                        onTaskAttachmentRemoved = viewModel::removeTaskAttachment,
                    )
                    Button(
                        onClick = {
                            viewModel.submitValidateAndEnqueue(
                                onEnqueuedNavigate = onBack,
                                onFinished = { result ->
                                    if (result is SubmitOfflineResult.Failed) {
                                        Toast.makeText(appContext, result.message, Toast.LENGTH_LONG).show()
                                    }
                                },
                            )
                        },
                        enabled = !state.isSubmitting,
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp),
                        elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
                    ) {
                        if (state.isSubmitting) CircularProgressIndicator(modifier = Modifier.height(20.dp))
                        else Text("Submit return to ramp")
                    }
                    Spacer(Modifier.height(24.dp))
                }
            }
        }
    }
}
