package com.nags.operations.ui.screens

import android.widget.Toast
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
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
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Scaffold
import androidx.compose.material3.SnackbarHost
import androidx.compose.material3.SnackbarHostState
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.activity.compose.BackHandler
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.data.db.entities.CustomerEntity
import com.nags.operations.ui.components.InlineSearchableDropdownField
import com.nags.operations.ui.components.AircraftTypePicker
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.SignatureField
import com.nags.operations.ui.components.SubmitWorkOrderAtdDialog
import com.nags.operations.ui.components.WorkOrderAtaPickerField
import com.nags.operations.ui.components.WorkOrderFlightSummaryCard
import com.nags.operations.ui.util.offsetSameAsFlight
import com.nags.operations.ui.workorder.CreateWorkOrderUiState
import com.nags.operations.ui.workorder.CreateWorkOrderViewModel
import com.nags.operations.ui.workorder.ServiceLineFormRow
import com.nags.operations.ui.workorder.ServiceLineSubmitFieldErrors
import com.nags.operations.ui.workorder.SubmitOfflineResult
import com.nags.operations.ui.workorder.SubmitValidationResult
import com.nags.operations.ui.workorder.TaskFormRow
import com.nags.operations.ui.workorder.TaskLineSubmitFieldErrors
import com.nags.operations.ui.workorder.WorkOrderFlightLoadState
import com.nags.operations.ui.workorder.FormSectionTitle
import com.nags.operations.ui.workorder.ServiceLineCard
import com.nags.operations.ui.workorder.ServiceLinesSectionHeading
import com.nags.operations.ui.workorder.TaskLineCard
import com.nags.operations.ui.workorder.TasksSectionHeading
import com.nags.operations.ui.workorder.fieldErrorSupportingText
import java.time.ZoneOffset
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CreateWorkOrderScreen(
    viewModel: CreateWorkOrderViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }

    BackHandler(onBack = onBack)

    Scaffold(
        snackbarHost = { SnackbarHost(snackbarHostState) },
        topBar = {
            TopAppBar(
                title = {
                    Text(
                        when {
                            state.isAdHocScratch -> "Create Ad Hoc Flight"
                            state.isUpdatingCachedUnderReviewWorkOrder -> "Update work order"
                            else -> "Work Order"
                        },
                        fontWeight = FontWeight.SemiBold,
                    )
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
                    title = "Couldn't load flight",
                    message = (state.flightLoad as WorkOrderFlightLoadState.Error).message,
                    onRetry = { viewModel.retryLoadFlight() },
                )
            }

            WorkOrderFlightLoadState.Ready -> CreateWorkOrderFormContent(
                modifier = Modifier.padding(padding),
                state = state,
                viewModel = viewModel,
                snackbarHostState = snackbarHostState,
                onClose = onBack,
            )
        }
    }
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun CreateWorkOrderFormContent(
    modifier: Modifier = Modifier,
    state: CreateWorkOrderUiState,
    viewModel: CreateWorkOrderViewModel,
    snackbarHostState: SnackbarHostState,
    onClose: () -> Unit,
) {
    val flight = state.flight ?: return
    val flightOffset = remember(flight.std) { offsetSameAsFlight(flight.std) }
    val scope = rememberCoroutineScope()
    val appCtx = LocalContext.current.applicationContext
    val submitErrs = state.submitFieldErrors
    var showSubmitAtdDialog by remember { mutableStateOf(false) }

    LaunchedEffect(state.submitValidationResult) {
        if (state.submitValidationResult is SubmitValidationResult.Passed) {
            showSubmitAtdDialog = true
            viewModel.clearSubmitValidationResult()
        }
    }

    if (showSubmitAtdDialog) {
        SubmitWorkOrderAtdDialog(
            defaultAtdIso = null,
            flightStdIso = flight.std,
            flightOffset = flightOffset,
            atdValidationError = submitErrs?.atd,
            onAtdIsoChanged = viewModel::clearAtdSubmitError,
            onDismiss = { showSubmitAtdDialog = false },
            onConfirm = { atdIso ->
                val errs = viewModel.confirmSubmitWithAtd(atdIso)
                if (errs == null) {
                    showSubmitAtdDialog = false
                    viewModel.enqueueSubmission(
                        onInstantNavigate = onClose,
                        onFinished = { outcome ->
                            when (outcome) {
                                is SubmitOfflineResult.Enqueued -> {
                                    // Navigated immediately; pending chip appears once Room flush completes.
                                }
                                is SubmitOfflineResult.Failed -> {
                                    Toast.makeText(appCtx, outcome.message, Toast.LENGTH_LONG).show()
                                }
                            }
                        },
                    )
                } else {
                    val needsFormFix = errs.customer != null || errs.aircraftType != null || errs.ata != null ||
                        errs.serviceLinesByKey.isNotEmpty() || errs.tasksByKey.isNotEmpty()
                    if (needsFormFix) {
                        showSubmitAtdDialog = false
                    }
                    scope.launch {
                        val msg = errs.atd?.lineSequence()?.firstOrNull()
                            ?: errs.ata?.lineSequence()?.firstOrNull()
                            ?: "Fix the highlighted fields on the form."
                        snackbarHostState.showSnackbar(msg)
                    }
                }
            },
        )
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        if (state.isAdHocScratch) {
            val customers = state.catalogCustomers
            val selected: CustomerEntity? =
                customers.firstOrNull { it.customerId == state.selectedCustomerId }
            InlineSearchableDropdownField(
                label = "Customer",
                selectedText = selected?.let { "${it.name} (${it.iataCode})" } ?: "",
                placeholder = "Search or select customer",
                options = customers,
                renderOption = { "${it.name} (${it.iataCode})" },
                onSelect = { viewModel.selectCustomer(it.customerId) },
                isError = submitErrs?.customer != null,
                supportingText = fieldErrorSupportingText(submitErrs?.customer),
            )
            if (customers.isEmpty()) {
                Text(
                    "Customers sync with catalogs — open Sync Center if the list is empty.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
        }

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

        OutlinedTextField(
            value = state.form.flightNumber,
            onValueChange = viewModel::updateFlightNumber,
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(14.dp),
            label = { Text("Flight number") },
            singleLine = true,
        )
        AircraftTypePicker(
            selectedId = state.form.aircraftTypeId,
            options = state.catalogAircraftTypes,
            onSelected = { viewModel.setAircraftType(it.aircraftTypeId) },
            isError = submitErrs?.aircraftType != null,
            supportingText = fieldErrorSupportingText(submitErrs?.aircraftType),
        )
        if (state.catalogAircraftTypes.isEmpty()) {
            Text(
                "Aircraft types sync with catalogs — pull to refresh on the flight list or open Sync Center.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        OutlinedTextField(
            value = state.form.aircraftTailNumber,
            onValueChange = viewModel::updateAircraftTailNumber,
            modifier = Modifier.fillMaxWidth(),
            shape = RoundedCornerShape(14.dp),
            label = { Text("Aircraft tail number") },
            singleLine = true,
        )
        WorkOrderAtaPickerField(
            ataIso = state.form.ataIso,
            staIso = flight.sta,
            flightOffset = flightOffset,
            onAtaConfirmed = { iso -> viewModel.updateForm { it.copy(ataIso = iso) } },
            isError = submitErrs?.ata != null,
            supportingText = fieldErrorSupportingText(submitErrs?.ata),
        )
        OutlinedTextField(
            value = state.form.remarks,
            onValueChange = { v -> viewModel.updateForm { it.copy(remarks = v) } },
            modifier = Modifier
                .fillMaxWidth()
                .height(160.dp),
            shape = RoundedCornerShape(14.dp),
            label = { Text("Remarks (optional)") },
            minLines = 5,
            maxLines = 8,
        )

        ServiceLinesSectionHeading(
            catalogsMissingServices = state.catalogServices.isEmpty(),
            catalogsMissingEmployees = state.catalogEmployees.isEmpty(),
        )
        if (state.form.serviceLines.isEmpty()) {
            Text(
                text = "No services yet. Services are optional — tap Add service when you need billable service lines.",
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
                lineErrors = submitErrs?.serviceLinesByKey[row.localKey],
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
                text = "No tasks yet. Tasks are optional — tap Add task when you need to record corrective work or store usage.",
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
                lineErrors = submitErrs?.tasksByKey[row.localKey],
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

        FormSectionTitle("Customer signature")
        SignatureField(
            signaturePng = state.form.customerSignaturePng,
            onChange = { png ->
                viewModel.updateForm { it.copy(customerSignaturePng = png) }
            },
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (state.showSaveAsDraftButton) {
                OutlinedButton(
                    onClick = {
                        viewModel.saveDraft { success, msg ->
                            if (success) {
                                onClose()
                            } else {
                                scope.launch { snackbarHostState.showSnackbar(msg) }
                            }
                        }
                    },
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(14.dp),
                ) {
                    Text("Save as draft")
                }
            } else if (state.activeDraftId != null) {
                OutlinedButton(
                    onClick = {
                        viewModel.saveDraft { success, msg ->
                            if (success) {
                                onClose()
                            } else {
                                scope.launch { snackbarHostState.showSnackbar(msg) }
                            }
                        }
                    },
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(14.dp),
                ) {
                    Text("Update draft")
                }
            }
            Button(
                onClick = { viewModel.submitDryRunValidate() },
                modifier = Modifier.weight(1f),
                shape = RoundedCornerShape(14.dp),
                elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
            ) {
                Text("Submit")
            }
        }

        Spacer(Modifier.height(24.dp))
    }
}

