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
import androidx.compose.material3.TextButton
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
import com.nags.operations.ui.components.WorkOrderAtaPickerField
import com.nags.operations.ui.components.WorkOrderDateTimePickerField
import com.nags.operations.ui.components.WorkOrderFlightSummaryCard
import com.nags.operations.ui.util.offsetSameAsFlight
import com.nags.operations.ui.workorder.CreateWorkOrderUiState
import com.nags.operations.ui.workorder.CreateWorkOrderViewModel
import com.nags.operations.ui.workorder.SubmitOfflineResult
import com.nags.operations.ui.workorder.WorkOrderFlightLoadState
import com.nags.operations.ui.workorder.WorkOrderWizardStep
import com.nags.operations.ui.workorder.FormSectionTitle
import com.nags.operations.ui.workorder.ServiceLineCard
import com.nags.operations.ui.workorder.ServiceLinesSectionHeading
import com.nags.operations.ui.workorder.TaskLineCard
import com.nags.operations.ui.workorder.TasksSectionHeading
import com.nags.operations.ui.workorder.fieldErrorSupportingText
import com.nags.operations.ui.workorder.firstWizardStepWithErrors
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CreateWorkOrderScreen(
    viewModel: CreateWorkOrderViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }

    BackHandler {
        if (!state.isSubmitting) onBack()
    }

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
                    IconButton(onClick = onBack, enabled = !state.isSubmitting) {
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
    var currentStep by remember(flight.id) { mutableStateOf(WorkOrderWizardStep.Flight) }
    val scrollState = rememberScrollState()

    BackHandler(
        enabled = currentStep != WorkOrderWizardStep.Flight && !state.isSubmitting,
    ) {
        currentStep = WorkOrderWizardStep.entries[currentStep.ordinal - 1]
    }
    LaunchedEffect(currentStep) {
        scrollState.scrollTo(0)
    }

    Column(
        modifier = modifier
            .fillMaxSize()
            .verticalScroll(scrollState)
            .padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(12.dp),
    ) {
        WorkOrderWizardStepper(
            currentStep = currentStep,
            onStepSelected = { selected ->
                if (selected.ordinal < currentStep.ordinal) currentStep = selected
            },
        )

        when (currentStep) {
            WorkOrderWizardStep.Flight -> {
                FormSectionTitle("Flight details")
        if (state.isAdHocScratch) {
            val customers = state.catalogCustomers
            val selected: CustomerEntity? =
                customers.firstOrNull { it.customerId == state.selectedCustomerId }
            InlineSearchableDropdownField(
                label = "Customer",
                selectedText = selected?.let { customerDisplay(it) } ?: "",
                placeholder = "Search or select customer",
                options = customers,
                renderOption = ::customerDisplay,
                onSelect = { viewModel.selectCustomer(it.customerId) },
                onClearSelection = { viewModel.selectCustomer(null) },
                hasSelection = state.selectedCustomerId != null,
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
            isError = submitErrs?.flightNumber != null,
            supportingText = fieldErrorSupportingText(submitErrs?.flightNumber),
        )
        AircraftTypePicker(
            selectedId = state.form.aircraftTypeId,
            options = state.catalogAircraftTypes,
            onSelected = { viewModel.setAircraftType(it.aircraftTypeId) },
            onCleared = { viewModel.setAircraftType(null) },
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
            isError = submitErrs?.aircraftTailNumber != null,
            supportingText = fieldErrorSupportingText(submitErrs?.aircraftTailNumber),
        )
        if (state.isAdHocScratch) {
            WorkOrderDateTimePickerField(
                iso = state.form.scheduledArrivalIso,
                label = "STA (Scheduled time of arrival)",
                placeholder = "Tap to set scheduled arrival",
                flightOffset = flightOffset,
                defaultInitialIso = flight.sta,
                onIsoConfirmed = viewModel::updateScratchScheduledArrival,
                isError = submitErrs?.scheduledArrival != null,
                supportingText = fieldErrorSupportingText(submitErrs?.scheduledArrival),
            )
            WorkOrderDateTimePickerField(
                iso = state.form.scheduledDepartureIso,
                label = "STD (Scheduled time of departure)",
                placeholder = "Tap to set scheduled departure",
                flightOffset = flightOffset,
                defaultInitialIso = state.form.scheduledArrivalIso.ifBlank { flight.std },
                onIsoConfirmed = viewModel::updateScratchScheduledDeparture,
                isError = submitErrs?.scheduledDeparture != null,
                supportingText = fieldErrorSupportingText(submitErrs?.scheduledDeparture),
            )
        }
        WorkOrderAtaPickerField(
            ataIso = state.form.ataIso,
            staIso = flight.sta,
            flightOffset = flightOffset,
            onAtaConfirmed = { iso -> viewModel.updateForm { it.copy(ataIso = iso) } },
            isError = submitErrs?.ata != null,
            supportingText = fieldErrorSupportingText(submitErrs?.ata),
        )
        WorkOrderDateTimePickerField(
            iso = state.form.atdIso,
            label = "ATD (Actual time of departure)",
            placeholder = "Tap to set departure date & time",
            flightOffset = flightOffset,
            defaultInitialIso = flight.std,
            onIsoConfirmed = { iso -> viewModel.updateForm { it.copy(atdIso = iso) } },
            isError = submitErrs?.atd != null,
            supportingText = fieldErrorSupportingText(submitErrs?.atd),
        )
        OutlinedTextField(
            value = state.form.remarks,
            onValueChange = { v ->
                viewModel.updateForm { it.copy(remarks = v.take(com.nags.operations.ui.workorder.WorkOrderFormLimits.Remarks)) }
            },
            modifier = Modifier
                .fillMaxWidth()
                .height(160.dp),
            shape = RoundedCornerShape(14.dp),
            label = { Text("Remarks (optional)") },
            minLines = 5,
            maxLines = 8,
            isError = submitErrs?.remarks != null,
            supportingText = fieldErrorSupportingText(submitErrs?.remarks),
        )
            }

            WorkOrderWizardStep.ServiceLines -> {
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
            }

            WorkOrderWizardStep.Tasks -> {
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
                onAttachmentAdded = { viewModel.addTaskAttachment(row.localKey, it) },
                onAttachmentRemoved = { viewModel.removeTaskAttachment(row.localKey, it) },
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
            }

            WorkOrderWizardStep.Signature -> {
        FormSectionTitle("Customer signature")
        state.form.existingCustomerSignatureName?.let { name ->
            Text(
                if (state.form.customerSignaturePng == null) {
                    "Existing signature: $name. Drawing a new signature will replace it."
                } else {
                    "The new signature will replace the existing signature: $name."
                },
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        SignatureField(
            signaturePng = state.form.customerSignaturePng,
            onChange = { png ->
                viewModel.updateForm { it.copy(customerSignaturePng = png) }
            },
        )
            }
        }

        val draftActionLabel = when {
            state.showSaveAsDraftButton -> "Save as draft"
            state.activeDraftId != null -> "Update draft"
            else -> null
        }
        if (draftActionLabel != null) {
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
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(14.dp),
                enabled = !state.isSubmitting,
            ) {
                Text(draftActionLabel)
            }
        }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (currentStep != WorkOrderWizardStep.Flight) {
                OutlinedButton(
                    onClick = {
                        currentStep = WorkOrderWizardStep.entries[currentStep.ordinal - 1]
                    },
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(14.dp),
                    enabled = !state.isSubmitting,
                ) {
                    Text("Previous")
                }
            }
            Button(
                onClick = {
                    if (currentStep != WorkOrderWizardStep.Signature) {
                        if (viewModel.validateWizardStep(currentStep)) {
                            currentStep = WorkOrderWizardStep.entries[currentStep.ordinal + 1]
                        } else {
                            scope.launch {
                                snackbarHostState.showSnackbar("Fix the highlighted fields before continuing.")
                            }
                        }
                    } else {
                        val errors = viewModel.confirmSubmitWithAtd(state.form.atdIso)
                        if (errors != null) {
                            currentStep = firstWizardStepWithErrors(errors)
                            scope.launch {
                                snackbarHostState.showSnackbar("Fix the highlighted fields before submitting.")
                            }
                        } else {
                            viewModel.enqueueSubmission(
                                onEnqueuedNavigate = onClose,
                                onFinished = { outcome ->
                                    if (outcome is SubmitOfflineResult.Failed) {
                                        Toast.makeText(
                                            appCtx,
                                            outcome.message,
                                            Toast.LENGTH_LONG,
                                        ).show()
                                    }
                                },
                            )
                        }
                    }
                },
                enabled = !state.isSubmitting,
                modifier = Modifier.weight(1f),
                shape = RoundedCornerShape(14.dp),
                elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
            ) {
                Text(
                    when {
                        currentStep != WorkOrderWizardStep.Signature -> "Next"
                        state.isUpdatingCachedUnderReviewWorkOrder -> "Update"
                        else -> "Create"
                    },
                )
            }
        }

        Spacer(Modifier.height(24.dp))
    }
}

@Composable
private fun WorkOrderWizardStepper(
    currentStep: WorkOrderWizardStep,
    onStepSelected: (WorkOrderWizardStep) -> Unit,
) {
    val labels = listOf("Flight", "Service lines", "Tasks", "Signature")
    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(2.dp),
    ) {
        WorkOrderWizardStep.entries.forEachIndexed { index, step ->
            val active = step == currentStep
            val completed = step.ordinal < currentStep.ordinal
            TextButton(
                onClick = { onStepSelected(step) },
                enabled = completed,
                modifier = Modifier.weight(1f),
                colors = ButtonDefaults.textButtonColors(
                    contentColor = MaterialTheme.colorScheme.primary,
                    disabledContentColor = if (active) {
                        MaterialTheme.colorScheme.primary
                    } else {
                        MaterialTheme.colorScheme.onSurfaceVariant
                    },
                ),
            ) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text(
                        text = if (completed) "✓" else (index + 1).toString(),
                        style = MaterialTheme.typography.titleMedium,
                        fontWeight = FontWeight.Bold,
                    )
                    Text(
                        text = labels[index],
                        style = MaterialTheme.typography.labelSmall,
                        fontWeight = if (active) FontWeight.Bold else FontWeight.Normal,
                        maxLines = 2,
                    )
                }
            }
        }
    }
}

private fun customerDisplay(customer: CustomerEntity): String =
    customer.iataCode?.takeIf { it.isNotBlank() }?.let { "${customer.name} ($it)" } ?: customer.name
