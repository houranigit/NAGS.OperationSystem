package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.rememberLazyListState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.outlined.Share
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Text
import androidx.compose.material3.pulltorefresh.PullToRefreshBox
import androidx.compose.material3.pulltorefresh.PullToRefreshDefaults
import androidx.compose.material3.pulltorefresh.rememberPullToRefreshState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberUpdatedState
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalLocale
import androidx.compose.ui.res.pluralStringResource
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import com.nags.operations.ui.components.flightStatusColors
import androidx.compose.ui.text.font.FontStyle
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleResumeEffect
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.repeatOnLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import com.nags.operations.AppGraph
import com.nags.operations.R
import com.nags.operations.data.FlightDiscoverySource
import com.nags.operations.data.FlightSharePost
import com.nags.operations.ui.AppViewModelFactory
import com.nags.operations.ui.components.EmptyState
import com.nags.operations.ui.components.FlightCard
import com.nags.operations.ui.components.InlineNotice
import com.nags.operations.ui.components.InlineNoticeKind
import com.nags.operations.ui.theme.LoadingIndicatorGray
import java.time.Instant
import java.time.OffsetDateTime
import java.time.ZoneId
import java.time.format.DateTimeFormatter
import java.time.format.FormatStyle
import java.util.Locale
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.distinctUntilChanged

/** Shared posts use the same details and operational journey as Search. */
@Composable
internal fun SharedFlightsScreen(graph: AppGraph, factory: AppViewModelFactory, subject: String?,
    bottomContentPadding: Dp, interactionEnabled: Boolean,
) {
    val shares: SharedFlightsViewModel = viewModel(key = "shared:$subject", factory = factory)
    val detailFactory = remember(factory) { factory.forDiscovery(FlightDiscoverySource.Search) }
    val flights: FlightsViewModel = viewModel(key = "shared-flight:$subject", factory = detailFactory)
    LifecycleResumeEffect(shares) {
        shares.refresh(false)
        onPauseOrDispose { }
    }
    FlightsScreen(viewModel = flights, discoverySource = FlightDiscoverySource.Search,
        interactionEnabled = interactionEnabled, bottomContentPadding = bottomContentPadding,
        onPublishShare = { flight, id, message, recipients -> graph.sharedFlightsRepository.publish(flight, id, message, recipients) },
        onSharePublished = { shares.refresh(false) },
        customFeed = { openFlight -> SharedFlightsFeed(shares, bottomContentPadding, interactionEnabled,
            onOpenFlight = openFlight, loadCustomerLogo = flights::customerLogo) })
}

@OptIn(ExperimentalMaterial3Api::class)
@Composable
private fun SharedFlightsFeed(viewModel: SharedFlightsViewModel, bottomContentPadding: Dp, enabled: Boolean,
    onOpenFlight: (String) -> Unit, loadCustomerLogo: (String) -> kotlinx.coroutines.flow.Flow<ByteArray?>,
) {
    val state by viewModel.state.collectAsStateWithLifecycle()
    var now by remember { mutableStateOf(Instant.now()) }
    val lifecycle = LocalLifecycleOwner.current
    LaunchedEffect(lifecycle, viewModel) {
        lifecycle.lifecycle.repeatOnLifecycle(Lifecycle.State.RESUMED) {
            now = Instant.now()
            while (true) {
                delay(30_000)
                now = Instant.now()
                viewModel.expire()
            }
        }
    }
    val listState = androidx.compose.runtime.key(state.stationId) { rememberLazyListState() }
    val currentState by rememberUpdatedState(state)
    LaunchedEffect(viewModel, listState) {
        snapshotFlow {
            val current = currentState
            val last = listState.layoutInfo.visibleItemsInfo.lastOrNull()?.index ?: -1
            if (current.loaded && current.hasMore && current.error == null && current.moreError == null &&
                !current.loading && !current.refreshing && !current.loadingMore &&
                (current.items.isEmpty() || last >= current.items.size - 3)) current.page else null
        }.distinctUntilChanged().collect { if (it != null) viewModel.loadMore() }
    }
    val refresh = rememberPullToRefreshState()
    PullToRefreshBox(isRefreshing = state.refreshing, state = refresh, onRefresh = { viewModel.refresh() },
        modifier = Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface),
        indicator = {
            PullToRefreshDefaults.Indicator(isRefreshing = state.refreshing, state = refresh,
                modifier = Modifier.align(Alignment.TopCenter), color = LoadingIndicatorGray)
        }) {
        HorizontalDivider(Modifier.align(Alignment.TopCenter), color = MaterialTheme.colorScheme.outlineVariant)
        LazyColumn(state = listState, modifier = Modifier.fillMaxSize(),
            contentPadding = PaddingValues(start = 8.dp, end = 16.dp, top = 16.dp, bottom = bottomContentPadding),
            verticalArrangement = Arrangement.spacedBy(24.dp)) {
            if (state.fromCache || !state.online) item(key = "cached") {
                InlineNotice(stringResource(R.string.flight_shares_cached), kind = InlineNoticeKind.Info)
            }
            state.error?.let { error -> item(key = "error") {
                InlineNotice(error, kind = InlineNoticeKind.Error, onRetry = { viewModel.refresh() })
            } }
            if (state.loading && state.items.isEmpty()) item(key = "loading") {
                Box(Modifier.fillMaxWidth().height(100.dp), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(color = LoadingIndicatorGray)
                }
            }
            else if (state.loaded && state.items.isEmpty()) item(key = "empty") {
                EmptyState(icon = Icons.Outlined.Share,
                    title = stringResource(R.string.flight_shares_empty),
                    message = stringResource(R.string.flight_shares_empty_hint))
            }
            items(state.items, key = { it.id }) { post ->
                FlightShareEntry(post, now, onClick = { if (enabled) onOpenFlight(post.flightId) },
                    loadCustomerLogo = loadCustomerLogo)
            }
            if (state.loadingMore) item(key = "paging") {
                Box(Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator(Modifier.size(24.dp), color = LoadingIndicatorGray)
                }
            }
            state.moreError?.let { error -> item(key = "paging-error") {
                InlineNotice(error, kind = InlineNoticeKind.Error, onRetry = viewModel::loadMore)
            } }
        }
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun FlightShareEntry(post: FlightSharePost, now: Instant, onClick: () -> Unit,
    loadCustomerLogo: (String) -> kotlinx.coroutines.flow.Flow<ByteArray?>,
) {
    val initials = remember(post.authorFullName) {
        post.authorFullName.trim().split(Regex("\\s+")).take(2)
            .mapNotNull { it.firstOrNull()?.toString() }.joinToString("").uppercase(Locale.ROOT)
    }
    val audience = if (post.isStationWide) {
        stringResource(R.string.flight_shares_with_station, (post.stationName?.takeIf { it.isNotBlank() } ?: post.flight.stationIataCode).trim().replace(Regex("\\s+"), "_"))
    } else {
        val first = post.recipients.firstOrNull()?.fullName.orEmpty()
        val others = (post.recipients.size - 1).coerceAtLeast(0)
        if (others == 0) stringResource(R.string.flight_shares_with_employee, first)
        else pluralStringResource(R.plurals.flight_shares_with_others, others, first, others)
    }
    val emphasized = if (post.isStationWide) listOf("@" + (post.stationName?.takeIf { it.isNotBlank() } ?: post.flight.stationIataCode).trim().replace(Regex("\\s+"), "_"))
        else listOfNotNull(post.recipients.firstOrNull()?.fullName, (post.recipients.size - 1).takeIf { it > 0 }?.toString())
    val audienceText = buildAnnotatedString {
        append(audience)
        if (!post.isStationWide && post.recipients.size > 1) {
            val name = post.recipients.first().fullName
            val end = audience.indexOf(name).takeIf { it >= 0 }?.plus(name.length)
            if (end != null) addStyle(SpanStyle(fontWeight = FontWeight.Bold), end, audience.length)
        }
        emphasized.forEach { token ->
            val start = audience.indexOf(token)
            if (start >= 0) addStyle(SpanStyle(fontWeight = FontWeight.Bold,
                color = if (post.isStationWide) Color(0xFF2979D5) else Color.Unspecified), start, start + token.length)
        }
    }
    Column {
    Row(Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(8.dp), verticalAlignment = Alignment.Top) {
        Box(Modifier.size(32.dp).background(flightStatusColors(post.flight.cardReportStatus(now = now)).stripe, CircleShape),
            contentAlignment = Alignment.Center) {
            Text(initials, fontSize = 12.sp, lineHeight = 16.sp, fontWeight = FontWeight.SemiBold, color = Color.White)
        }
        Column(Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(0.dp)) {
            FlowRow(horizontalArrangement = Arrangement.spacedBy(6.dp), verticalArrangement = Arrangement.spacedBy(2.dp)) {
                Text(post.authorFullName, modifier = Modifier.alignByBaseline(), fontSize = 14.sp, lineHeight = 18.sp, fontWeight = FontWeight.SemiBold,
                    color = MaterialTheme.colorScheme.onSurface)
                Text(shareTimestamp(post, now), modifier = Modifier.alignByBaseline(), fontSize = 11.sp, lineHeight = 18.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            Text(audienceText, fontSize = 11.sp, lineHeight = 15.sp, color = MaterialTheme.colorScheme.onSurfaceVariant)
            post.message?.takeIf { it.isNotBlank() }?.let {
                Text("“" + it + "”", fontSize = 13.sp, lineHeight = 19.sp, fontStyle = FontStyle.Italic,
                    color = MaterialTheme.colorScheme.onSurface, modifier = Modifier)
            }
            FlightCard(post.flight, onClick, modifier = Modifier,
                loadCustomerLogo = loadCustomerLogo, compact = true)
        }
    }
    HorizontalDivider(Modifier.padding(top = 12.dp), color = MaterialTheme.colorScheme.outlineVariant.copy(alpha = 0.5f))
    }
}

@Composable
private fun shareTimestamp(post: FlightSharePost, now: Instant): String {
    val age = sharePostAge(post.createdAtUtc, now)
    return when (age.kind) {
        ShareAgeKind.JustNow -> stringResource(R.string.flight_shares_just_now)
        ShareAgeKind.Minutes -> pluralStringResource(R.plurals.flight_shares_minutes_ago, age.count, age.count)
        ShareAgeKind.Hours -> pluralStringResource(R.plurals.flight_shares_hours_ago, age.count, age.count)
        ShareAgeKind.Days -> pluralStringResource(R.plurals.flight_shares_days_ago, age.count, age.count)
        ShareAgeKind.Date -> {
            val locale = LocalLocale.current.platformLocale
            remember(post.createdAtUtc, post.flight.stationTimeZoneId, locale) {
                runCatching { OffsetDateTime.parse(post.createdAtUtc).atZoneSameInstant(ZoneId.of(post.flight.stationTimeZoneId))
                    .format(DateTimeFormatter.ofLocalizedDate(FormatStyle.MEDIUM).withLocale(locale)) }.getOrDefault("")
            }
        }
    }
}
