package com.nags.operations.ui.screens

import android.widget.Toast
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Add
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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.activity.compose.BackHandler
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.WorkOrderFlightSummaryCard
import com.nags.operations.ui.util.offsetSameAsFlight
import com.nags.operations.ui.workorder.FormSectionTitle
import com.nags.operations.ui.workorder.ReturnToRampViewModel
import com.nags.operations.ui.workorder.ServiceLineCard
import com.nags.operations.ui.workorder.ServiceLinesSectionHeading
import com.nags.operations.ui.workorder.SubmitOfflineResult
import com.nags.operations.ui.workorder.TaskLineCard
import com.nags.operations.ui.workorder.TasksSectionHeading
import com.nags.operations.ui.workorder.WorkOrderFlightLoadState

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun ReturnToRampScreen(
    viewModel: ReturnToRampViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val appCtx = LocalContext.current.applicationContext

    BackHandler(onBack = onBack)

    Scaffold(
        topBar = {
            TopAppBar(
                title = {
                    Text("Return to ramp", fontWeight = FontWeight.SemiBold)
                },
                navigationIcon = {
                    IconButton(onClick = onBack) {
                        Icon(Icons.AutoMirrored.Filled.ArrowBack, contentDescription = "Back")
                    }
                },
            )
        },
    ) { padding ->
        when (state.flightLoad) {
            WorkOrderFlightLoadState.Loading -> Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentAlignment = Alignment.Center,
            ) {
                CircularProgressIndicator()
            }
            is WorkOrderFlightLoadState.Error -> Box(
                modifier = Modifier
                    .fillMaxSize()
                    .padding(padding),
                contentAlignment = Alignment.Center,
            ) {
                ErrorState(
                    title = "Can't open return to ramp",
                    message = (state.flightLoad as WorkOrderFlightLoadState.Error).message,
                    onRetry = { viewModel.retryLoadFlight() },
                )
            }
            WorkOrderFlightLoadState.Ready -> {
                val flight = state.flight ?: return@Scaffold
                val flightOffset = remember(flight.std) { offsetSameAsFlight(flight.std) }
                Column(
                    modifier = Modifier
                        .fillMaxSize()
                        .padding(padding)
                        .verticalScroll(rememberScrollState())
                        .padding(16.dp),
                    verticalArrangement = Arrangement.spacedBy(12.dp),
                ) {
                    Text(
                        "Add billable services and/or tasks to record the return-to-ramp portion of this turnaround. " +
                            "They are stored with the return-to-ramp flag.",
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
                    state.formLevelError?.let { msg ->
                        Text(
                            msg,
                            color = MaterialTheme.colorScheme.error,
                            style = MaterialTheme.typography.bodySmall,
                        )
                    }
                    ServiceLinesSectionHeading(
                        catalogsMissingServices = state.catalogServices.isEmpty(),
                        catalogsMissingEmployees = state.catalogEmployees.isEmpty(),
                    )
                    if (state.form.serviceLines.isEmpty()) {
                        Text(
                            "Tap Add service to record a billable line for this return to ramp.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    state.form.serviceLines.forEachIndexed { index, row ->
                        if (index > 0) Spacer(Modifier.height(12.dp))
                        ServiceLineCard(
                            lineNumber = index + 1,
                            flightOffset = flightOffset,
                            scheduleAnchorIso = flight.sta,
                            row = row,
                            lineErrors = state.submitFieldErrors?.serviceLinesByKey[row.localKey],
                            services = state.catalogServices,
                            employees = state.catalogEmployees,
                            onChange = viewModel::replaceServiceLine,
                            onRemove = { viewModel.removeServiceLine(row.localKey) },
                            canRemove = true,
                        )
                    }
                    Button(
                        onClick = { viewModel.addServiceLine() },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp),
                        elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
                    ) {
                        Icon(Icons.Default.Add, contentDescription = null)
                        Spacer(Modifier.width(10.dp))
                        Text("Add service")
                    }
                    FormSectionTitle("Tasks")
                    TasksSectionHeading(
                        catalogsMissingEmployees = state.catalogEmployees.isEmpty(),
                        catalogsMissingTools = state.catalogTools.isEmpty(),
                        catalogsMissingMaterials = state.catalogMaterials.isEmpty(),
                        catalogsMissingGeneralSupports = state.catalogGeneralSupports.isEmpty(),
                    )
                    if (state.form.tasks.isEmpty()) {
                        Text(
                            "Tap Add task when you need corrective actions, photos, or store usage for this return to ramp.",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant,
                        )
                    }
                    state.form.tasks.forEachIndexed { index, row ->
                        if (index > 0) Spacer(Modifier.height(12.dp))
                        TaskLineCard(
                            lineNumber = index + 1,
                            flightOffset = flightOffset,
                            scheduleAnchorIso = flight.sta,
                            row = row,
                            lineErrors = state.submitFieldErrors?.tasksByKey[row.localKey],
                            employees = state.catalogEmployees,
                            tools = state.catalogTools,
                            materials = state.catalogMaterials,
                            generalSupports = state.catalogGeneralSupports,
                            onChange = viewModel::replaceTask,
                            onRemove = { viewModel.removeTask(row.localKey) },
                            canRemove = true,
                        )
                    }
                    Button(
                        onClick = { viewModel.addTask() },
                        modifier = Modifier.fillMaxWidth(),
                        shape = RoundedCornerShape(14.dp),
                        elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
                    ) {
                        Icon(Icons.Default.Add, contentDescription = null)
                        Spacer(Modifier.width(10.dp))
                        Text("Add task")
                    }
                    Button(
                        onClick = {
                            viewModel.submitValidateAndEnqueue(
                                onInstantNavigate = onBack,
                                onFinished = { outcome ->
                                    when (outcome) {
                                        is SubmitOfflineResult.Enqueued -> { }
                                        is SubmitOfflineResult.Failed -> {
                                            Toast.makeText(appCtx, outcome.message, Toast.LENGTH_LONG).show()
                                        }
                                    }
                                },
                            )
                        },
                        modifier = Modifier
                            .fillMaxWidth()
                            .padding(top = 8.dp),
                        shape = RoundedCornerShape(14.dp),
                        elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
                    ) {
                        Text("Submit")
                    }
                    Spacer(Modifier.height(24.dp))
                }
            }
        }
    }
}
