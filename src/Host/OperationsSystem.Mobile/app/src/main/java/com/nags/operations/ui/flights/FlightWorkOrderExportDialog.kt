package com.nags.operations.ui.flights

import android.widget.Toast
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import com.nags.operations.AppGraph
import com.nags.operations.R
import com.nags.operations.data.FlightDetail
import com.nags.operations.data.repo.FlightWorkOrderPdf
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.launch

@Composable
internal fun FlightWorkOrderExportDialog(flight: FlightDetail, online: Boolean, onDismiss: () -> Unit) {
    val context = LocalContext.current
    val exports = remember(context) { AppGraph.get(context).flightWorkOrderExports }
    val scope = rememberCoroutineScope()
    var pdf by rememberSaveable { mutableStateOf<FlightWorkOrderPdf?>(null) }
    var pageCount by remember { mutableIntStateOf(0) }
    var attempt by remember { mutableIntStateOf(0) }
    var loadFailed by remember { mutableStateOf(false) }
    var actionError by remember { mutableStateOf<Int?>(null) }
    var saving by remember { mutableStateOf(false) }
    var sharing by remember { mutableStateOf(false) }
    var choosingLocation by rememberSaveable { mutableStateOf(false) }

    LaunchedEffect(online, attempt) {
        if (pageCount > 0 || pdf == null && !online) return@LaunchedEffect
        loadFailed = false
        try {
            val file = pdf ?: exports.download(flight.id, flight.flightNumber)
            val count = FlightWorkOrderPdfRenderer(exports, file).pageCount()
            pdf = file
            pageCount = count
        } catch (cancelled: CancellationException) {
            throw cancelled
        } catch (_: Exception) {
            pdf = null
            loadFailed = true
        }
    }

    val picker = rememberLauncherForActivityResult(ActivityResultContracts.CreateDocument("application/pdf")) { destination ->
        val file = pdf
        choosingLocation = false
        if (destination != null && file != null) {
            saving = true
            scope.launch {
                try {
                    exports.saveCopy(file, destination)
                    Toast.makeText(context, R.string.flight_wo_saved, Toast.LENGTH_SHORT).show()
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (_: Exception) {
                    actionError = R.string.flight_wo_save_failed
                } finally {
                    saving = false
                }
            }
        }
    }

    val file = pdf
    if (file == null || pageCount == 0) {
        val problem = when {
            !online && file == null -> R.string.flight_wo_online_required
            loadFailed -> R.string.flight_wo_export_failed
            else -> null
        }
        AlertDialog(
            onDismissRequest = onDismiss,
            containerColor = MaterialTheme.colorScheme.surface,
            title = { Text(stringResource(R.string.flight_wo_preview)) },
            text = {
                if (problem != null) Text(stringResource(problem))
                else Row(horizontalArrangement = Arrangement.spacedBy(10.dp), verticalAlignment = Alignment.CenterVertically) {
                    CircularProgressIndicator(Modifier.size(18.dp), strokeWidth = 2.dp)
                    Text(stringResource(R.string.flight_wo_preparing))
                }
            },
            confirmButton = {
                if (loadFailed && online) TextButton(onClick = { loadFailed = false; attempt++ }) {
                    Text(stringResource(R.string.flights_retry))
                }
            },
            dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(R.string.flights_cancel)) } },
        )
        return
    }

    val renderer = remember(exports, file) { FlightWorkOrderPdfRenderer(exports, file) }
    val busy = saving || sharing || choosingLocation
    FlightWorkOrderPreview(
        flightNumber = flight.flightNumber,
        renderer = renderer,
        pageCount = pageCount,
        busy = busy,
        saving = saving,
        actionError = actionError,
        onDismiss = { if (!busy) onDismiss() },
        onSave = {
            if (!saving && !sharing && !choosingLocation) {
                actionError = null
                choosingLocation = true
                try {
                    picker.launch(file.fileName)
                } catch (_: Exception) {
                    choosingLocation = false
                    actionError = R.string.flight_wo_save_failed
                }
            }
        },
        onShare = {
            if (!saving && !sharing && !choosingLocation) {
                actionError = null
                sharing = true
                scope.launch {
                    try {
                        context.startActivity(exports.shareIntent(file))
                    } catch (cancelled: CancellationException) {
                        throw cancelled
                    } catch (_: Exception) {
                        actionError = R.string.flight_wo_share_failed
                    } finally {
                        sharing = false
                    }
                }
            }
        },
        onReload = {
            pdf = null
            pageCount = 0
            actionError = null
            attempt++
        },
    )
}
