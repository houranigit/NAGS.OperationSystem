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
import androidx.compose.material3.AlertDialog
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
import com.nags.operations.data.db.entities.allowedPerformedServiceIds
import com.nags.operations.ui.components.InlineSearchableDropdownField
import com.nags.operations.ui.components.AircraftTypePicker
import com.nags.operations.ui.components.ErrorState
import com.nags.operations.ui.components.SignatureField
import com.nags.operations.ui.components.SubmitWorkOrderAtdDialog
import com.nags.operations.ui.components.WorkOrderAtaPickerField
import com.nags.operations.ui.components.WorkOrderDateTimePickerField
import com.nags.operations.ui.components.WorkOrderFlightSummaryCard
import com.nags.operations.ui.util.offsetSameAsFlight
import com.nags.operations.ui.workorder.CreateWorkOrderUiState
import com.nags.operations.ui.workorder.CreateWorkOrderViewModel
import com.nags.operations.ui.workorder.DraftSaveResult
import com.nags.operations.ui.workorder.SubmitOfflineResult
import com.nags.operations.ui.workorder.WorkOrderFlightLoadState
import com.nags.operations.ui.workorder.WorkOrderWizardStep
import com.nags.operations.ui.workorder.FormSectionTitle
import com.nags.operations.ui.workorder.ServiceLineCard
import com.nags.operations.ui.workorder.ServiceLinesSectionHeading
import com.nags.operations.ui.workorder.TaskLineCard
import com.nags.operations.ui.workorder.TasksSectionHeading
import com.nags.operations.ui.workorder.fieldErrorSupportingText
import kotlinx.coroutines.launch

@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun CreateWorkOrderScreen(
    viewModel: CreateWorkOrderViewModel,
    onBack: () -> Unit,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    val snackbarHostState = remember { SnackbarHostState() }
    val scope = rememberCoroutineScope()
    var showExitDialog by remember { mutableStateOf(false) }
    val busy = state.isSavingDraft || state.isSubmitting

    fun requestExit() {
        if (busy) return
        if (state.hasUnsavedChanges) {
            showExitDialog = true
        } else {
            onBack()
        }
    }

    BackHandler(enabled = !state.isAtdDialogVisible) {
        requestExit()
    }

    if (showExitDialog) {
        AlertDialog(
            onDismissRequest = { if (!busy) showExitDialog = false },
            title = { Text("Leave work order?") },
            text = {
                Text(
                    "You have changes that are not in the latest local draft. " +
                        "Save them to continue later, or discard only these unsaved changes.",
                )
            },
            confirmButton = {
                Button(
                    onClick = {
                        viewModel.saveDraft { result ->
                            when (result) {
                                is DraftSaveResult.Saved -> {
                                    if (result.isCurrent) {
                                        showExitDialog = false
                                        onBack()
                                    } else {
                                        scope.launch {
                                            snackbarHostState.showSnackbar(result.message)
                                        }
                                    }
                                }
                                is DraftSaveResult.Failed -> scope.launch {
                                    snackbarHostState.showSnackbar(result.message)
                                }
                            }
                        }
                    },
                    enabled = !busy,
                ) {
                    Text(if (state.activeDraftId == null) "Save as draft" else "Update draft")
                }
            },
            dismissButton = {
                Row(horizontalArrangement = Arrangement.spacedBy(4.dp)) {
                    TextButton(
                        onClick = {
                            showExitDialog = false
                            onBack()
                        },
                        enabled = !busy,
                    ) {
                        Text("Discard changes")
                    }
                    TextButton(
                        onClick = { showExitDialog = false },
                        enabled = !busy,
                    ) {
                        Text("Keep editing")
                    }
                }
            },
        )
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
                    IconButton(onClick = ::requestExit, enabled = !busy) {
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
    val busy = state.isSavingDraft || state.isSubmitting
    val currentStep = state.wizardStep
    val scrollState = rememberScrollState()

    BackHandler(
        enabled = currentStep != WorkOrderWizardStep.Flight && !busy && !state.isAtdDialogVisible,
    ) {
        viewModel.moveToPreviousWizardStep()
    }
    LaunchedEffect(currentStep) {
        scrollState.scrollTo(0)
    }

    if (state.isAtdDialogVisible) {
        SubmitWorkOrderAtdDialog(
            defaultAtdIso = state.form.atdIso.takeIf { it.isNotBlank() },
            flightOffset = flightOffset,
            isBusy = busy,
            atdValidationError = submitErrs?.atd,
            onAtdIsoChanged = viewModel::clearAtdSubmitError,
            onDismiss = viewModel::dismissAtdDialog,
            onSaveDraft = { atdIso ->
                viewModel.saveDraftWithAtd(atdIso) { result ->
                    when (result) {
                        is DraftSaveResult.Saved -> {
                            if (result.isCurrent) {
                                viewModel.dismissAtdDialog()
                                onClose()
                            } else {
                                scope.launch {
                                    snackbarHostState.showSnackbar(result.message)
                                }
                            }
                        }
                        is DraftSaveResult.Failed -> scope.launch {
                            snackbarHostState.showSnackbar(result.message)
                        }
                    }
                }
            },
            onConfirm = { atdIso ->
                val errors = viewModel.confirmSubmitWithAtd(atdIso)
                if (errors == null) {
                    viewModel.enqueueSubmission(
                        onEnqueuedNavigate = {
                            viewModel.dismissAtdDialog()
                            onClose()
                        },
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
                } else if (errors.atd != null) {
                    scope.launch {
                        snackbarHostState.showSnackbar(
                            errors.atd.lineSequence().firstOrNull()
                                ?: "Correct the departure time before submitting.",
                        )
                    }
                } else {
                    viewModel.routeToFirstWizardError(errors)
                    scope.launch {
                        snackbarHostState.showSnackbar("Fix the highlighted fields before submitting.")
                    }
                }
            },
        )
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
            enabled = !busy,
            onStepSelected = viewModel::selectCompletedWizardStep,
        )

        when (currentStep) {
            WorkOrderWizardStep.Flight -> {
                FormSectionTitle("Flight details")

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
            performedServicesUnavailable = state.catalogServices.allowedPerformedServiceIds().isEmpty(),
            catalogsMissingEmployees = state.catalogEmployees.isEmpty(),
        )
        if (state.form.serviceLines.isEmpty()) {
            Text(
                text = if (flight.isPerLanding) {
                    "No performed services yet. Add only services actually performed; adding one makes this flight On Call."
                } else {
                    "No performed services yet. Add only services actually performed for this flight."
                },
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
            enabled = state.catalogServices.allowedPerformedServiceIds().isNotEmpty(),
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
            state.activeDraftId != null -> "Update draft"
            state.showSaveAsDraftButton -> "Save as draft"
            else -> null
        }
        if (draftActionLabel != null) {
            OutlinedButton(
                onClick = {
                    viewModel.saveDraft { result ->
                        when (result) {
                            is DraftSaveResult.Saved -> {
                                if (result.isCurrent) {
                                    onClose()
                                } else {
                                    scope.launch {
                                        snackbarHostState.showSnackbar(result.message)
                                    }
                                }
                            }
                            is DraftSaveResult.Failed -> scope.launch {
                                snackbarHostState.showSnackbar(result.message)
                            }
                        }
                    }
                },
                modifier = Modifier.fillMaxWidth(),
                shape = RoundedCornerShape(14.dp),
                enabled = !busy,
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
                    onClick = viewModel::moveToPreviousWizardStep,
                    modifier = Modifier.weight(1f),
                    shape = RoundedCornerShape(14.dp),
                    enabled = !busy,
                ) {
                    Text("Previous")
                }
            }
            Button(
                onClick = {
                    if (currentStep != WorkOrderWizardStep.Signature) {
                        val stepBeingSaved = currentStep
                        val stepIsValid = viewModel.prepareWizardStepForNext(stepBeingSaved)
                        viewModel.saveDraft { result ->
                            when (result) {
                                is DraftSaveResult.Failed -> scope.launch {
                                    snackbarHostState.showSnackbar(result.message)
                                }
                                is DraftSaveResult.Saved -> when {
                                    !result.isCurrent -> scope.launch {
                                        snackbarHostState.showSnackbar(result.message)
                                    }
                                    !stepIsValid -> scope.launch {
                                        snackbarHostState.showSnackbar(
                                            "Draft saved. Fix the highlighted fields before continuing.",
                                        )
                                    }
                                    else -> viewModel.advanceWizardStepAfterCheckpoint(stepBeingSaved)
                                }
                            }
                        }
                    } else {
                        viewModel.saveDraft { result ->
                            when (result) {
                                is DraftSaveResult.Failed -> scope.launch {
                                    snackbarHostState.showSnackbar(result.message)
                                }
                                is DraftSaveResult.Saved -> when {
                                    !result.isCurrent -> scope.launch {
                                        snackbarHostState.showSnackbar(result.message)
                                    }
                                    else -> {
                                        val errors = viewModel.validateBeforeAtdDialog()
                                        if (errors != null) {
                                            viewModel.routeToFirstWizardError(errors)
                                            scope.launch {
                                                snackbarHostState.showSnackbar(
                                                    "Draft saved. Fix the highlighted fields before submitting.",
                                                )
                                            }
                                        } else {
                                            viewModel.showAtdDialog()
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                enabled = !busy,
                modifier = Modifier.weight(1f),
                shape = RoundedCornerShape(14.dp),
                elevation = ButtonDefaults.buttonElevation(defaultElevation = 2.dp),
            ) {
                Text(
                    if (currentStep == WorkOrderWizardStep.Signature) "Submit" else "Next",
                )
            }
        }

        Spacer(Modifier.height(24.dp))
    }
}

@Composable
private fun WorkOrderWizardStepper(
    currentStep: WorkOrderWizardStep,
    enabled: Boolean,
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
                enabled = completed && enabled,
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
