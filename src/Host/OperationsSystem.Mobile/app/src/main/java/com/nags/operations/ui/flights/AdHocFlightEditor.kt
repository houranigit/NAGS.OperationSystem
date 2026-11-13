package com.nags.operations.ui.flights

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.nags.operations.data.*
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import java.time.LocalDate
import java.time.LocalDateTime
import java.time.OffsetDateTime
import java.time.ZoneId
import java.util.Locale
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.flow.flowOf

/** Flight details use the same normal Arrival/work editor and atomic local save as scheduled flights. */
@Composable
internal fun AdHocFlightEditor(viewModel: FlightsViewModel, initialFlight: FlightDetail, onClose: () -> Unit,
    initialNavigationKey: String? = null, progressStatus: FlightProgressStatus? = null) {
    val repository = checkNotNull(viewModel.adHocRepository)
    val customers by viewModel.adHocCustomers.collectAsStateWithLifecycle(initialValue = emptyList())
    val aircraft by viewModel.arrivalAircraftTypes.collectAsStateWithLifecycle(initialValue = emptyList())
    val catalogs by viewModel.operationsCatalogs.collectAsStateWithLifecycle(initialValue = FlightOperationsCatalogs())
    val arrivals by (viewModel.arrivals?.changes ?: flowOf(emptyList())).collectAsStateWithLifecycle(initialValue = emptyList())
    val departures by (viewModel.departures?.changes ?: flowOf(emptyList())).collectAsStateWithLifecycle(initialValue = emptyList())
    val services by (viewModel.services?.changes ?: flowOf(emptyList())).collectAsStateWithLifecycle(initialValue = emptyList())
    val tasks by (viewModel.tasks?.changes ?: flowOf(emptyList())).collectAsStateWithLifecycle(initialValue = emptyList())
    val drafts by (viewModel.services?.drafts ?: flowOf(emptyList())).collectAsStateWithLifecycle(initialValue = emptyList())
    val planning by repository.pending.collectAsStateWithLifecycle(initialValue = emptyList())
    val context by repository.context.collectAsStateWithLifecycle(initialValue = null)
    val zone = remember(initialFlight.stationTimeZoneId) { ZoneId.of(initialFlight.stationTimeZoneId) }
    var customerId by rememberSaveable(initialFlight.id) { mutableStateOf(initialFlight.customerId) }
    var flightNumber by rememberSaveable(initialFlight.id) { mutableStateOf(initialFlight.flightNumber) }
    var sta by rememberSaveable(initialFlight.id) { mutableStateOf(OffsetDateTime.parse(initialFlight.scheduledArrivalUtc).atZoneSameInstant(zone).toLocalDateTime().toString()) }
    var std by rememberSaveable(initialFlight.id) { mutableStateOf(OffsetDateTime.parse(initialFlight.scheduledDepartureUtc).atZoneSameInstant(zone).toLocalDateTime().toString()) }
    var attempted by rememberSaveable(initialFlight.id) { mutableStateOf(false) }
    var picker by rememberSaveable(initialFlight.id) { mutableStateOf<String?>(null) }
    var initialized by rememberSaveable(initialFlight.id) { mutableStateOf(false) }
    LaunchedEffect(initialFlight.id) {
        if (!initialized) {
            repository.row(initialFlight.id)?.let { row ->
                val input = com.nags.operations.data.api.HttpClientFactory.json.decodeFromString<AdHocFlightInput>(row.inputJson)
                customerId = input.customerId; flightNumber = input.flightNumber
                sta = OffsetDateTime.parse(input.scheduledArrivalUtc).atZoneSameInstant(zone).toLocalDateTime().toString()
                std = OffsetDateTime.parse(input.scheduledDepartureUtc).atZoneSameInstant(zone).toLocalDateTime().toString()
            }
            initialized = true
        }
        try { repository.refreshContext() }
        catch (cancelled: CancellationException) { throw cancelled }
        catch (_: Exception) { /* Last authorized station context remains available offline. */ }
    }
    if (!initialized) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        return
    }
    val pending = planning.firstOrNull { it.flightId == initialFlight.id }
    val customer = customers.firstOrNull { it.id == customerId }
    val input = AdHocFlightInput(customerId, flightNumber,
        arrivalInputTime(sta, initialFlight.scheduledArrivalUtc, zone), arrivalInputTime(std, initialFlight.scheduledDepartureUtc, zone)).normalized()
    val baseline = AdHocFlightInput(initialFlight.customerId, initialFlight.flightNumber,
        initialFlight.scheduledArrivalUtc, initialFlight.scheduledDepartureUtc).normalized()
    val requestedInWindow = initialFlight.copy(scheduledArrivalUtc = input.scheduledArrivalUtc,
        scheduledDepartureUtc = input.scheduledDepartureUtc).isInFlightWindow(java.time.Instant.now())
    val hasOperationalIntent = arrivals.any { it.flightId == initialFlight.id } || services.any { it.flightId == initialFlight.id } ||
        tasks.any { it.flightId == initialFlight.id } || departures.any { it.flightId == initialFlight.id }
    val scopeValid = context?.stationId == initialFlight.stationId && initialFlight.isInFlightWindow(java.time.Instant.now())
    val editable = initialFlight.canEditFlightDetails && !hasOperationalIntent && scopeValid
    val flight = initialFlight.copy(customerId = customerId, customerName = customer?.name ?: initialFlight.customerName,
        customerIataCode = customer?.iataCode ?: initialFlight.customerIataCode, flightNumber = flightNumber,
        scheduledArrivalUtc = input.scheduledArrivalUtc.ifBlank { initialFlight.scheduledArrivalUtc },
        scheduledDepartureUtc = input.scheduledDepartureUtc.ifBlank { initialFlight.scheduledDepartureUtc },
        canSaveArrival = initialFlight.canSaveArrival && scopeValid, canSaveDeparture = initialFlight.canSaveDeparture && scopeValid,
        canSaveServices = initialFlight.canSaveServices && scopeValid, canSaveTasks = initialFlight.canSaveTasks && scopeValid,
        canEditFlightDetails = editable)
    val stationValidation = stringResource(R.string.adhoc_verify_station)
    val windowValidation = stringResource(R.string.adhoc_window_error)
    FlightOperationsForm(flight, aircraft, viewModel::customerLogo, onClose, catalogs = catalogs.forStation(flight.stationId),
        pending = arrivals.firstOrNull { it.flightId == flight.id }, pendingDeparture = departures.firstOrNull { it.flightId == flight.id },
        pendingServices = services.filter { it.flightId == flight.id }, pendingTasks = tasks.filter { it.flightId == flight.id },
        serviceFormDraft = drafts.firstOrNull { it.flightId == flight.id }, progressStatus = progressStatus,
        loadSignature = viewModel::departureSignature,
        onSaveArrival = { detail, value -> checkNotNull(viewModel.arrivals).save(detail, value) },
        onReviewArrival = { viewModel.reviewFlightInput(flight.id) }, onSaveDeparture = viewModel::saveDeparture,
        onSaveServices = viewModel::saveServices, onSaveTasks = viewModel::saveTasks,
        onReviewServices = { viewModel.reviewFlightInput(flight.id) }, onReviewTasks = { viewModel.reviewFlightInput(flight.id) },
        loadServiceAttachment = viewModel::serviceAttachment, loadPendingServiceAttachment = viewModel::pendingServiceAttachment,
        loadTaskAttachment = viewModel::taskAttachment, loadPendingTaskAttachment = viewModel::pendingTaskAttachment,
        onSaveBatch = { saveSections -> repository.save(initialFlight.copy(customerName = flight.customerName,
            customerIataCode = flight.customerIataCode, canEditFlightDetails = editable), input, saveSections) },
        onSaveReviewed = viewModel::saveReviewed, onDiscardReviewed = { viewModel.discardReviewed(flight.id, it) },
        initialNavigationKey = initialNavigationKey,
        identityHasChanges = initialFlight.rowVersion == LOCAL_ADHOC_VERSION || input != baseline || pending != null,
        identityValidation = { attempted = true; if (!scopeValid) stationValidation
            else input.validationMessage() ?: if (!requestedInWindow) windowValidation else null },
        identityContent = if (!initialFlight.canEditFlightDetails && initialFlight.rowVersion != LOCAL_ADHOC_VERSION) null else { {
            Column(Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                Text(stringResource(R.string.adhoc_flight_details), style = MaterialTheme.typography.bodySmall, fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(horizontal = 14.dp))
                FlightFormFieldGroup {
                    FlightFormChoiceRow(stringResource(R.string.adhoc_customer), (customer?.name ?: initialFlight.customerName).ifBlank { stringResource(R.string.flight_form_required_field) }, stringResource(R.string.adhoc_choose_customer),
                        isPlaceholder = customerId.isBlank(), enabled = editable,
                        errorMessage = if (attempted && !validArrivalId(customerId)) stringResource(R.string.adhoc_customer_required) else null,
                        onClick = { picker = "customer" })
                    FlightFormDivider()
                    FlightFormTextRow(stringResource(R.string.home_search_flight_number), flightNumber, { flightNumber = it.uppercase(Locale.ROOT) }, stringResource(R.string.flight_form_required_field),
                        enabled = editable, errorMessage = if (attempted && flightNumber.isBlank()) stringResource(R.string.adhoc_flight_number_required)
                            else if (attempted && flightNumber.trim().length > 12) stringResource(R.string.adhoc_flight_number_length) else null)
                    FlightFormDivider()
                    FlightFormDateTimeField(sta, "STA", stringResource(R.string.adhoc_choose_sta), enabled = editable,
                        errorMessage = if (attempted && input.scheduledArrivalUtc.isBlank()) stringResource(R.string.adhoc_sta_invalid)
                            else if (attempted && input.validationMessage() == null && !requestedInWindow) stringResource(R.string.adhoc_window_hint) else null,
                        onClick = { picker = "sta" })
                    FlightFormDivider()
                    FlightFormDateTimeField(std, "STD", stringResource(R.string.adhoc_choose_std), enabled = editable,
                        errorMessage = if (attempted && (input.scheduledDepartureUtc.isBlank() || input.scheduledArrivalUtc.isNotBlank() &&
                            runCatching { parseFlightInstant(input.scheduledDepartureUtc) < parseFlightInstant(input.scheduledArrivalUtc) }.getOrDefault(true)))
                            stringResource(R.string.adhoc_std_invalid) else null, onClick = { picker = "std" })
                }
            }
        } },
        identityNote = {
            AdHocFlightCreationNote(initialFlight, zone)
            pending?.let { InlineNotice(it.error ?: stringResource(R.string.adhoc_pending_details),
                kind = if (it.status == "pending") InlineNoticeKind.Info else InlineNoticeKind.Warning) }
            if (!scopeValid) InlineNotice(stringResource(R.string.adhoc_retained_scope_notice), kind = InlineNoticeKind.Warning)
        })
    if (picker == "customer") FlightWorkPicker(stringResource(R.string.adhoc_customer), stringResource(R.string.adhoc_search_customers), stringResource(R.string.adhoc_customers_empty),
        customers.map { FlightWorkChoice(it.id, it.name, it.iataCode.orEmpty()) }, customerId,
        onSelect = { customerId = it }, onDismiss = { picker = null })
    if (picker == "sta" || picker == "std") {
        val arrival = picker == "sta"
        FlightFormDateTimeSheet(LocalDateTime.parse(if (arrival) sta else std), LocalDate.now(zone).year,
            onValueChange = { if (arrival) sta = it.toString() else std = it.toString() }, onDismiss = { picker = null })
    }
}

@Composable
private fun AdHocFlightCreationNote(flight: FlightDetail, zone: ZoneId) {
    if (flight.rowVersion == LOCAL_ADHOC_VERSION) return
    val name = flight.createdByDisplayName?.takeIf { it.isNotBlank() } ?: return
    val locale = LocalLocale.current.platformLocale
    val template = stringResource(R.string.flight_submission_timestamp)
    val timestamp = remember(flight.createdAtUtc, zone, locale, template) {
        runCatching {
            val at = parseFlightInstant(flight.createdAtUtc).atZone(zone)
            String.format(locale, template, at.format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)),
                at.format(DateTimeFormatter.ofPattern("HH:mm", locale)))
        }.getOrNull()
    }
    val prefix = stringResource(R.string.adhoc_created_by)
    val on = stringResource(R.string.flight_submission_note_on)
    Text(buildAnnotatedString {
        append(prefix)
        withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(name) }
        if (timestamp != null) {
            append(on)
            withStyle(SpanStyle(fontWeight = FontWeight.SemiBold)) { append(timestamp) }
        }
        append(".")
    }, Modifier.fillMaxWidth().padding(horizontal = 14.dp, vertical = 4.dp),
        style = MaterialTheme.typography.bodySmall.copy(fontSize = 12.sp, lineHeight = 18.sp),
        color = MaterialTheme.colorScheme.onSurfaceVariant)
}
