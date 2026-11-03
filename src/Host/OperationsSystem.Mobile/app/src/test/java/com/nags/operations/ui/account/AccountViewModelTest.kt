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
import org.junit.Assert.*
import org.junit.Test

class AccountViewModelTest {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Unconfined)

    @After
    fun tearDown() = scope.cancel()

    @Test
    fun profileLoadingAndFailureDoNotInventOrReplaceIdentity() {
        val response = CompletableDeferred<AuthenticatedUser>()
        var fail = false
        val vm = AccountViewModel(MutableStateFlow(true), {
            if (fail) throw IOException()
            response.await()
        }, scope)
        assertNull(vm.state.value.profile)
        assertTrue(vm.state.value.isLoading)
        val user = AuthenticatedUser("user", "staff@example.test", "Staff", "StationStaff")
        response.complete(user)
        assertEquals(user, vm.state.value.profile)
        fail = true
        vm.refresh()
        assertEquals(user, vm.state.value.profile)
        assertNotNull(vm.state.value.error)
        assertFalse(vm.state.value.isLoading)
    }

    @Test
    fun reconnectLoadsTheProfileWithoutOfflineWrites() {
        val online = MutableStateFlow(false)
        var reads = 0
        val vm = AccountViewModel(online, {
            reads++
            AuthenticatedUser("user", "staff@example.test", "Staff", "StationStaff")
        }, scope)
        vm.refresh()
        assertEquals(0, reads)
        online.value = true
        assertEquals(1, reads)
        assertNotNull(vm.state.value.profile)
    }
}
