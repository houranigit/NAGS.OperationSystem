package com.nags.operations.ui.account

import com.nags.operations.data.AuthenticatedUser
import java.io.IOException
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.flow.MutableStateFlow
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class AccountViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)
    private val online = MutableStateFlow(true)
    private var serverProfile = profile(false)
    private val writes = mutableListOf<Boolean>()

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun missingProfileCannotBeEditedWhileTheSavedValueLoads() {
        val response = CompletableDeferred<AuthenticatedUser>()
        val vm = model(fetch = { response.await() })
        assertNull(vm.state.value.profile)
        assertTrue(vm.state.value.isLoading)
        assertFalse(vm.state.value.canEditPreference)

        vm.setReceiveWorkOrderSubmissionEmails(true)
        assertTrue(writes.isEmpty())
        response.complete(profile(true))
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertTrue(vm.state.value.canEditPreference)
    }

    @Test
    fun preferenceRemainsAtItsConfirmedValueUntilSaveSucceeds() {
        val response = CompletableDeferred<Unit>()
        val vm = model(save = { writes += it; response.await() })
        vm.setReceiveWorkOrderSubmissionEmails(true)

        assertEquals(listOf(true), writes)
        assertTrue(vm.state.value.isSaving)
        assertFalse(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertFalse(vm.state.value.canEditPreference)
        assertFalse(vm.state.value.saved)

        response.complete(Unit)
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertTrue(vm.state.value.saved)
        assertTrue(vm.state.value.canEditPreference)
    }

    @Test
    fun enablingAndDisablingBothPersistToTheAccount() {
        val vm = model()
        vm.setReceiveWorkOrderSubmissionEmails(true)
        vm.setReceiveWorkOrderSubmissionEmails(false)

        assertEquals(listOf(true, false), writes)
        assertFalse(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertTrue(vm.state.value.saved)
    }

    @Test
    fun failedSavePreservesLastConfirmedValueAndRequiresServerReconciliation() {
        val vm = model(save = {
            // The server accepted a write but its response was lost.
            serverProfile = profile(it)
            throw IOException("Connection lost")
        })
        vm.setReceiveWorkOrderSubmissionEmails(true)

        assertFalse(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertEquals(AccountError.SaveUnconfirmed, vm.state.value.error)
        assertFalse(vm.state.value.canEditPreference)
        assertFalse(vm.state.value.saved)

        vm.refresh()
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertNull(vm.state.value.error)
        assertTrue(vm.state.value.canEditPreference)
    }

    @Test
    fun failedOptOutDoesNotFalselyDisplayEmailsAsDisabled() {
        serverProfile = profile(true)
        val vm = model(save = { throw IOException("Server unreachable") })
        vm.setReceiveWorkOrderSubmissionEmails(false)

        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertEquals(AccountError.SaveUnconfirmed, vm.state.value.error)
        assertFalse(vm.state.value.saved)
    }

    @Test
    fun offlineProfileCannotCreateOrQueueAnOptInAndLoadsOnReconnect() {
        online.value = false
        var reads = 0
        val vm = model(fetch = { reads++; serverProfile })
        vm.setReceiveWorkOrderSubmissionEmails(true)
        vm.refresh()
        assertEquals(0, reads)
        assertTrue(writes.isEmpty())
        assertNull(vm.state.value.profile)

        serverProfile = profile(true)
        online.value = true
        assertEquals(1, reads)
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertTrue(writes.isEmpty())
    }

    @Test
    fun losingConnectionDisablesChangesAndReconnectRefreshesPortalEdits() {
        val vm = model()
        online.value = false
        vm.setReceiveWorkOrderSubmissionEmails(true)
        assertTrue(writes.isEmpty())
        assertFalse(vm.state.value.canEditPreference)

        serverProfile = profile(true)
        online.value = true
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
    }

    @Test
    fun returningToProfileReloadsAPreferenceChangedInThePortal() {
        val vm = model()
        serverProfile = profile(true)
        vm.refresh()
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        serverProfile = profile(false)
        vm.refresh()
        assertFalse(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
    }

    @Test
    fun failedLoadDoesNotInventDefaultOffAndCanBeRetried() {
        var fail = true
        val vm = model(fetch = {
            if (fail) throw IOException("Offline")
            profile(true)
        })
        assertNull(vm.state.value.profile)
        assertEquals(AccountError.LoadFailed, vm.state.value.error)
        assertFalse(vm.state.value.canEditPreference)
        fail = false
        vm.refresh()
        assertTrue(vm.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        assertNull(vm.state.value.error)
    }

    @Test
    fun duplicateTapsCannotStartConcurrentWrites() {
        val response = CompletableDeferred<Unit>()
        val vm = model(save = { writes += it; response.await() })
        vm.setReceiveWorkOrderSubmissionEmails(true)
        vm.setReceiveWorkOrderSubmissionEmails(true)
        vm.setReceiveWorkOrderSubmissionEmails(false)
        assertEquals(listOf(true), writes)
        response.complete(Unit)
    }

    @Test
    fun newlyOpenedProfileDoesNotReuseAnotherAccountsPreference() {
        serverProfile = profile(true)
        val first = model()
        assertTrue(first.state.value.profile!!.receiveWorkOrderSubmissionEmails)
        serverProfile = profile(false).copy(id = "another-user", email = "another@example.test")
        val second = model()
        assertEquals("another-user", second.state.value.profile!!.id)
        assertFalse(second.state.value.profile!!.receiveWorkOrderSubmissionEmails)
    }

    private fun model(
        fetch: suspend () -> AuthenticatedUser = { serverProfile },
        save: suspend (Boolean) -> Unit = { writes += it; serverProfile = profile(it) },
    ) = AccountViewModel(online, fetch, save, scope)

    private fun profile(enabled: Boolean) = AuthenticatedUser(
        id = "employee-user",
        email = "employee@example.test",
        displayName = "Employee",
        userType = "Employee",
        receiveWorkOrderSubmissionEmails = enabled,
    )
}
