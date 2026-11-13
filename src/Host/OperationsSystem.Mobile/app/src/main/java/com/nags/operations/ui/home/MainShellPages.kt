package com.nags.operations.ui.home

import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.horizontalScroll
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.selection.selectable
import androidx.compose.foundation.selection.selectableGroup
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ManageSearch
import androidx.compose.material.icons.filled.Search
import androidx.compose.material.icons.filled.Close
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Text
import androidx.compose.material3.TopAppBar
import androidx.compose.material3.TopAppBarDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.drawBehind
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.nags.operations.R
import com.nags.operations.ui.flights.FlightTabSearchHeader

/** Creation entry point only; flight creation is not implemented by this shell. */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
internal fun AdHocFlightShell(onClose: () -> Unit) {
    Scaffold(
        containerColor = MaterialTheme.colorScheme.surface,
        topBar = {
            TopAppBar(
                title = { Text(stringResource(R.string.home_create_adhoc),
                    style = MaterialTheme.typography.titleMedium) },
                navigationIcon = {
                    IconButton(onClick = onClose) {
                        Icon(Icons.Default.Close, contentDescription = stringResource(R.string.home_close_adhoc))
                    }
                },
                colors = TopAppBarDefaults.topAppBarColors(containerColor = MaterialTheme.colorScheme.surface),
            )
        },
    ) { padding ->
        Box(Modifier.fillMaxSize().padding(padding))
    }
}

/** Presentation-only destinations; their queries do not request or filter any data. */
@Composable
internal fun MainShellPage(page: Int) {
    val keyboard = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current
    DisposableEffect(page, keyboard, focusManager) {
        onDispose {
            keyboard?.hide()
            focusManager.clearFocus()
        }
    }
    Column(Modifier.fillMaxSize().background(MaterialTheme.colorScheme.surface)) {
        when (page) {
            1 -> InvitationsShellHeader()
            2 -> ExpandedShellSearch(page = page, placeholder = stringResource(R.string.home_search_flight_number))
            3 -> ExpandedShellSearch(page = page, placeholder = stringResource(R.string.home_search_my_work))
        }
    }
}

@Composable
private fun InvitationsShellHeader() {
    var selectedTab by rememberSaveable { mutableIntStateOf(0) }
    var query by rememberSaveable { mutableStateOf("") }
    var isSearchOpen by rememberSaveable { mutableStateOf(false) }
    val keyboard = LocalSoftwareKeyboardController.current
    val focusManager = LocalFocusManager.current
    fun closeSearch() {
        keyboard?.hide()
        focusManager.clearFocus()
        query = ""
        isSearchOpen = false
    }
    BackHandler(enabled = isSearchOpen) { closeSearch() }
    val description = stringResource(R.string.home_search_invitations)
    FlightTabSearchHeader(
        isSearchOpen = isSearchOpen,
        query = query,
        onQueryChange = { query = it },
        onOpenSearch = { isSearchOpen = true },
        onCloseSearch = ::closeSearch,
        searchDescription = description,
        placeholder = description,
        openSearchDescription = description,
        searchAtPhysicalRight = true,
        searchSurface = shellSearchSurface(),
    ) {
        InvitationsShellTabs(selectedTab = selectedTab, onSelectTab = { selectedTab = it }, enabled = !isSearchOpen)
    }
}

@Composable
private fun ExpandedShellSearch(page: Int, placeholder: String) {
    var query by rememberSaveable(page) { mutableStateOf("") }
    FlightTabSearchHeader(
        isSearchOpen = true,
        query = query,
        onQueryChange = { query = it },
        onOpenSearch = {},
        onCloseSearch = {},
        searchDescription = placeholder,
        placeholder = placeholder,
        showCloseButton = false,
        autoFocusOnOpen = false,
        searchIcon = if (page == 2) Icons.AutoMirrored.Filled.ManageSearch else Icons.Default.Search,
        searchAtPhysicalRight = true,
        searchSurface = shellSearchSurface(),
        tabs = {},
    )
}

@Composable
private fun InvitationsShellTabs(selectedTab: Int, onSelectTab: (Int) -> Unit, enabled: Boolean) {
    val labels = listOf(stringResource(R.string.home_invitations_received), stringResource(R.string.home_invitations_sent))
    val active = MaterialTheme.colorScheme.primary
    val inactive = MaterialTheme.colorScheme.onSurfaceVariant
    Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp)
        .horizontalScroll(rememberScrollState()).selectableGroup()) {
        labels.forEachIndexed { index, label ->
            val selected = selectedTab == index
            Box(Modifier.widthIn(min = 48.dp).height(48.dp)
                .selectable(selected = selected, enabled = enabled, onClick = { onSelectTab(index) }, role = Role.Tab)
                .padding(horizontal = 8.dp)
                .drawBehind {
                    if (selected) drawRect(active, Offset(0f, size.height - 2.dp.toPx()), Size(size.width, 2.dp.toPx()))
                }, contentAlignment = Alignment.Center) {
                Text(label, style = MaterialTheme.typography.bodyMedium.copy(fontSize = 13.sp),
                    fontWeight = FontWeight.Bold, color = if (selected) active else inactive,
                    maxLines = 1, softWrap = false, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun shellSearchSurface(): Color = if (MaterialTheme.colorScheme.surface.luminance() < 0.5f) {
    MaterialTheme.colorScheme.surfaceVariant
} else {
    Color(0xFFF2F3F5)
}
