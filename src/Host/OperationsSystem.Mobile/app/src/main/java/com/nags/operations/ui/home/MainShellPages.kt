package com.nags.operations.ui.home

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import com.nags.operations.data.userMessage
import com.nags.operations.ui.theme.LoadingIndicatorGray

/** New AdHoc identity and normal operational input share one durable local Save. */
@Composable
internal fun AdHocFlightShell(graph: com.nags.operations.AppGraph, factory: com.nags.operations.ui.AppViewModelFactory,
    onClose: () -> Unit, flightId: String? = null) {
    val id = rememberSaveable(flightId) { flightId ?: java.util.UUID.randomUUID().toString() }
    val viewModel: com.nags.operations.ui.flights.FlightsViewModel = androidx.lifecycle.viewmodel.compose.viewModel(
        key = "adhoc:$id", factory = factory)
    var flight by androidx.compose.runtime.remember(id) { mutableStateOf<com.nags.operations.data.FlightDetail?>(null) }
    var error by androidx.compose.runtime.remember(id) { mutableStateOf<String?>(null) }
    var retry by rememberSaveable(id) { mutableIntStateOf(0) }
    androidx.compose.runtime.LaunchedEffect(id, retry) {
        error = null
        try {
            if (flightId != null) {
                flight = graph.adHocFlights.recoveryFlight(id) ?: graph.flightsRepository.flight(id)
            } else {
                if (graph.networkMonitor.isOnline.value && graph.adHocFlights.cachedContext()?.canCreate != true) {
                    try { graph.adHocFlights.refreshContext() }
                    catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
                    catch (failure: Exception) { if (graph.adHocFlights.cachedContext() == null) throw failure }
                }
                val at = java.time.Instant.now().truncatedTo(java.time.temporal.ChronoUnit.MINUTES)
                flight = graph.adHocFlights.newFlight(id, com.nags.operations.data.AdHocFlightInput("", "", at.toString(), at.plusSeconds(3600).toString()))
            }
        } catch (cancelled: kotlinx.coroutines.CancellationException) { throw cancelled }
        catch (failure: Exception) { error = failure.userMessage() }
    }
    val loaded = flight
    if (loaded != null) com.nags.operations.ui.flights.AdHocFlightEditor(viewModel, loaded, onClose)
    else Column(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface).padding(16.dp)) {
        androidx.compose.material3.TextButton(onClick = onClose) { Text("Close") }
        if (error != null) com.nags.operations.ui.components.InlineNotice(checkNotNull(error),
            kind = com.nags.operations.ui.components.InlineNoticeKind.Warning, onRetry = { retry++ })
        else Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { androidx.compose.material3.CircularProgressIndicator(color = LoadingIndicatorGray) }
    }
}
