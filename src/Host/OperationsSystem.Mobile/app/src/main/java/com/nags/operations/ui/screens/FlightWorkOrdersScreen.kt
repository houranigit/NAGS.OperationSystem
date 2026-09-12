package com.nags.operations.ui.screens

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedCard
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TopAppBar
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import com.nags.operations.data.WorkOrderDetailWireDto
import com.nags.operations.data.WorkOrderServiceLineWireDto
import com.nags.operations.data.WorkOrderTaskResourceWireDto
import com.nags.operations.data.WorkOrderTaskWireDto
import com.nags.operations.data.api.MobileApi
import com.nags.operations.data.api.WorkOrderListItemWireDto
import com.nags.operations.data.repo.FlightsRepository
import com.nags.operations.data.userMessage
import com.nags.operations.ui.util.formatIsoForDisplay
import com.nags.operations.ui.util.userTimeZone
import com.nags.operations.ui.workorder.printWorkOrderPdf
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

/** Read-only access stays available after completion and beyond the mobile authoring window. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun FlightWorkOrdersScreen(
    flightId: String,
    api: MobileApi,
    flightsRepository: FlightsRepository,
    onBack: () -> Unit,
) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var orders by remember(flightId) { mutableStateOf(emptyList<WorkOrderListItemWireDto>()) }
    var selected by remember(flightId) { mutableStateOf<WorkOrderDetailWireDto?>(null) }
    var selectedId by remember(flightId) { mutableStateOf<String?>(null) }
    var totalCount by remember(flightId) { mutableStateOf(0L) }
    var page by remember(flightId) { mutableStateOf(0) }
    var loading by remember(flightId) { mutableStateOf(true) }
    var detailLoading by remember(flightId) { mutableStateOf(false) }
    var printing by remember(flightId) { mutableStateOf(false) }
    var completed by remember(flightId) { mutableStateOf(false) }
    var error by remember(flightId) { mutableStateOf<String?>(null) }

    suspend fun loadOrders(reset: Boolean) {
        loading = true
        error = null
        try {
            val nextPage = if (reset) 1 else page + 1
            val response = api.workOrdersForFlight(flightId, nextPage)
            orders = if (reset) response.items else (orders + response.items).distinctBy { it.id }
            page = nextPage
            totalCount = response.totalCount
            if (reset) {
                // A fresh list supersedes cached content, including work orders that are no longer visible.
                selectedId = orders.firstOrNull()?.id
                selected = null
                if (selectedId != null) selected = api.workOrderById(requireNotNull(selectedId))
                completed = api.flightById(flightId).status == "Completed"
            }
        } catch (exception: CancellationException) {
            throw exception
        } catch (exception: Exception) {
            error = exception.userMessage()
        } finally {
            loading = false
        }
    }

    LaunchedEffect(flightId) {
        selected = flightsRepository.findWorkOrderFlight(flightId)?.cachedMyWorkOrder
        selectedId = selected?.id
        loadOrders(reset = true)
    }

    Scaffold(
        topBar = {
            TopAppBar(
                title = { Text("Flight work orders") },
                navigationIcon = {
                    IconButton(onClick = onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, "Back") }
                },
            )
        },
    ) { padding ->
        Column(
            Modifier.padding(padding).verticalScroll(rememberScrollState()).padding(16.dp),
            verticalArrangement = Arrangement.spacedBy(12.dp),
        ) {
            if (loading) CircularProgressIndicator()
            error?.let { message ->
                Text(message, color = MaterialTheme.colorScheme.error)
                if (selected != null) Text("Showing the last available work order details.")
                TextButton(onClick = { scope.launch { loadOrders(reset = true) } }, enabled = !loading) { Text("Refresh") }
            }
            if (!loading && orders.isEmpty() && selected == null) Text("No work orders are available for this flight.")
            orders.forEach { order ->
                OutlinedCard(
                    onClick = {
                        selectedId = order.id
                        selected = null
                        detailLoading = true
                        scope.launch {
                            try {
                                val detail = api.workOrderById(order.id)
                                if (selectedId == order.id) {
                                    selected = detail
                                    error = null
                                }
                            } catch (exception: CancellationException) {
                                throw exception
                            } catch (exception: Exception) {
                                if (selectedId == order.id) error = exception.userMessage()
                            } finally {
                                if (selectedId == order.id) detailLoading = false
                            }
                        }
                    },
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(4.dp)) {
                        Text(order.approvalNumber ?: "${order.type} work order", style = MaterialTheme.typography.titleSmall)
                        Text("${order.status} · ${order.ownerName ?: "Employee"}")
                        Text(formatIsoForDisplay(order.createdAtUtc), style = MaterialTheme.typography.bodySmall)
                    }
                }
            }
            if (orders.size.toLong() < totalCount) {
                TextButton(onClick = { scope.launch { loadOrders(reset = false) } }, enabled = !loading) { Text("Load more") }
            }
            if (completed) {
                Button(
                    onClick = {
                        printing = true
                        error = null
                        scope.launch {
                            try {
                                val bytes = api.approvedWorkOrderPdf(flightId, userTimeZone().id)
                                printWorkOrderPdf(context, flightId, bytes)
                            } catch (exception: CancellationException) {
                                throw exception
                            } catch (exception: Exception) {
                                error = exception.userMessage()
                            } finally {
                                printing = false
                            }
                        }
                    },
                    enabled = !printing,
                    modifier = Modifier.fillMaxWidth(),
                ) { Text(if (printing) "Preparing PDF…" else "Print approved WO PDF") }
                Text("Print or save the approved flight work order as a PDF.", style = MaterialTheme.typography.bodySmall)
            }
            if (detailLoading) CircularProgressIndicator()
            selected?.let { WorkOrderReadOnlyDetails(it) }
        }
    }
}

@Composable
private fun WorkOrderReadOnlyDetails(order: WorkOrderDetailWireDto) {
    HorizontalDivider()
    Text(order.approvalNumber ?: "Work order", style = MaterialTheme.typography.headlineSmall)
    Text("${order.status} · ${order.type}")
    Text("${order.customerName} · ${order.stationIata}")
    Text("Flight ${order.actualFlightNumber.ifBlank { order.plannedFlightNumber }}")
    Text("${order.aircraftTypeModel.orEmpty()} ${order.aircraftTailNumber.orEmpty()}".trim())
    Text("ATA: ${order.actualArrivalUtc?.let(::formatIsoForDisplay) ?: "—"}")
    Text("ATD: ${order.actualDepartureUtc?.let(::formatIsoForDisplay) ?: "—"}")
    order.canceledAtUtc?.let { Text("Canceled: ${formatIsoForDisplay(it)}") }
    order.cancellationReason?.takeIf(String::isNotBlank)?.let { Text("Cancellation reason: $it") }
    order.remarks?.takeIf(String::isNotBlank)?.let { Text("Remarks: $it") }
    WorkOrderActivity(order.serviceLines.filterNot { it.isReturnToRamp }, order.tasks.filterNot { it.isReturnToRamp })
    order.returnToRamps.forEachIndexed { index, occurrence ->
        Text("Return to ramp ${index + 1}", style = MaterialTheme.typography.titleMedium)
        PeriodText(occurrence.fromUtc, occurrence.toUtc)
        occurrence.description?.takeIf(String::isNotBlank)?.let { Text(it) }
        WorkOrderActivity(occurrence.serviceLines, occurrence.tasks)
    }
    // Old server responses may include only flat return-to-ramp rows.
    if (order.returnToRamps.isEmpty()) {
        WorkOrderActivity(order.serviceLines.filter { it.isReturnToRamp }, order.tasks.filter { it.isReturnToRamp })
    }
    order.customerSignature?.let { Text("Customer signed: ${formatIsoForDisplay(it.signedAtUtc)}") }
}

@Composable
private fun WorkOrderActivity(services: List<WorkOrderServiceLineWireDto>, tasks: List<WorkOrderTaskWireDto>) {
    services.forEach { service ->
        OutlinedCard(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(service.serviceName, style = MaterialTheme.typography.titleMedium)
                PeriodText(service.fromUtc, service.toUtc)
                service.description?.takeIf(String::isNotBlank)?.let { Text("Notes: $it") }
                service.effectivePerformedBy.forEach { employee ->
                    Text(employee.fullName)
                    PeriodText(employee.fromUtc ?: service.fromUtc, employee.toUtc ?: service.toUtc)
                }
                service.attachments.forEach { Text("Attachment: ${it.originalFileName}") }
            }
        }
    }
    tasks.forEach { task ->
        OutlinedCard(Modifier.fillMaxWidth()) {
            Column(Modifier.padding(12.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text("${task.taskType} task", style = MaterialTheme.typography.titleMedium)
                PeriodText(task.fromUtc, task.toUtc)
                task.description?.takeIf(String::isNotBlank)?.let { Text(it) }
                task.employees.forEach { employee ->
                    Text(employee.fullName)
                    PeriodText(employee.fromUtc ?: task.fromUtc, employee.toUtc ?: task.toUtc)
                }
                ResourceDetails("Tool", task.tools)
                ResourceDetails("Material", task.materials)
                ResourceDetails("General support", task.generalSupports)
                task.attachments.forEach { Text("Attachment: ${it.originalFileName}") }
            }
        }
    }
}

@Composable
private fun ResourceDetails(label: String, resources: List<WorkOrderTaskResourceWireDto>) {
    resources.forEach { resource ->
        Text("$label: ${resource.name}" + (resource.quantity?.let { " · $it" } ?: ""))
        resource.fromUtc?.let { PeriodText(it, resource.toUtc) }
        resource.description?.takeIf(String::isNotBlank)?.let { Text("Notes: $it") }
    }
}

@Composable
private fun PeriodText(from: String, to: String?) {
    Text("${formatIsoForDisplay(from)} → ${to?.let(::formatIsoForDisplay) ?: "Open-ended"}", style = MaterialTheme.typography.bodySmall)
}
