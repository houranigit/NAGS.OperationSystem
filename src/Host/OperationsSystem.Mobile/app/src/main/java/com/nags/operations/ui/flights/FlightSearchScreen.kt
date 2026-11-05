package com.nags.operations.ui.flights

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.WindowInsets
import androidx.compose.foundation.layout.WindowInsetsSides
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.imePadding
import androidx.compose.foundation.layout.navigationBars
import androidx.compose.foundation.layout.windowInsetsPadding
import androidx.compose.foundation.layout.only
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.statusBarsPadding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material.icons.filled.Cancel
import androidx.compose.material.icons.filled.Search
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.ScaffoldDefaults
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.platform.LocalFocusManager
import androidx.compose.ui.platform.LocalSoftwareKeyboardController
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import com.nags.operations.R

/** Search has its own page; the results presentation is a separate design step. */
@Composable
internal fun FlightSearchScreen(onBack: () -> Unit) {
    var query by rememberSaveable { mutableStateOf("") }
    val focusRequester = remember { FocusRequester() }
    val focusManager = LocalFocusManager.current
    val keyboard = LocalSoftwareKeyboardController.current
    val searchDescription = stringResource(R.string.flights_search)
    val foreground = Color(0xFF17181B)
    val textStyle = MaterialTheme.typography.bodyLarge.copy(color = foreground)

    LaunchedEffect(Unit) { focusRequester.requestFocus() }

    fun goBack() {
        keyboard?.hide()
        focusManager.clearFocus()
        onBack()
    }

    Scaffold(containerColor = Color.White,
        contentWindowInsets = ScaffoldDefaults.contentWindowInsets.only(WindowInsetsSides.Horizontal)) { padding ->
        Column(Modifier.fillMaxSize().padding(padding)
            .windowInsetsPadding(WindowInsets.navigationBars.only(WindowInsetsSides.Bottom)).imePadding()) {
            Row(Modifier.fillMaxWidth().statusBarsPadding().padding(horizontal = 12.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically) {
                IconButton(onClick = ::goBack, modifier = Modifier.size(48.dp)) {
                    Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.account_back),
                        tint = foreground, modifier = Modifier.size(24.dp))
                }
                BasicTextField(value = query, onValueChange = { query = it }, singleLine = true,
                    textStyle = textStyle, cursorBrush = SolidColor(Color(0xFF007AFF)),
                    keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
                    keyboardActions = KeyboardActions(onSearch = {
                        keyboard?.hide()
                        focusManager.clearFocus()
                    }),
                    modifier = Modifier.weight(1f).heightIn(min = 48.dp)
                        .clip(RoundedCornerShape(14.dp)).background(Color(0xFFF3F4F6))
                        .focusRequester(focusRequester).semantics { contentDescription = searchDescription },
                    decorationBox = { innerTextField ->
                        Row(Modifier.fillMaxWidth().heightIn(min = 48.dp).padding(start = 12.dp, end = 4.dp),
                            verticalAlignment = Alignment.CenterVertically) {
                            Icon(Icons.Default.Search, contentDescription = null,
                                tint = Color(0xFF787D84), modifier = Modifier.size(20.dp))
                            Box(Modifier.weight(1f).padding(horizontal = 10.dp, vertical = 12.dp)) {
                                if (query.isEmpty()) {
                                    Text(stringResource(R.string.flights_search_placeholder),
                                        style = textStyle, color = Color(0xFF787D84),
                                        maxLines = 1, overflow = TextOverflow.Ellipsis)
                                }
                                innerTextField()
                            }
                            if (query.isNotEmpty()) {
                                IconButton(onClick = {
                                    query = ""
                                    focusRequester.requestFocus()
                                }, modifier = Modifier.size(48.dp)) {
                                    Icon(Icons.Default.Cancel, stringResource(R.string.flights_search_clear),
                                        tint = Color(0xFF787D84), modifier = Modifier.size(20.dp))
                                }
                            }
                        }
                    })
            }
        }
    }
}
