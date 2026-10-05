"""Linux x86_64 worker-only permanent syscall socket denial. No model imports."""
import ctypes
import errno
import platform
import os
import signal

def parent_death_kill(expected_parent):
    libc=ctypes.CDLL(None,use_errno=True)
    if libc.prctl(1,signal.SIGKILL,0,0,0)!=0:
        raise OSError(ctypes.get_errno(),'PDEATHSIG unavailable; abort before Engine')
    if type(expected_parent) is not int or os.getppid()!=expected_parent:
        raise RuntimeError('runner parent changed; abort before Engine')

def install():
    if platform.system() != 'Linux' or platform.machine() != 'x86_64':
        raise RuntimeError('NETWORK_GUARD_UNSUPPORTED_PLATFORM; abort before Engine')
    class Filter(ctypes.Structure):
        _fields_ = [('code', ctypes.c_ushort), ('jt', ctypes.c_ubyte), ('jf', ctypes.c_ubyte), ('k', ctypes.c_uint)]
    class Program(ctypes.Structure):
        _fields_ = [('length', ctypes.c_ushort), ('filter', ctypes.POINTER(Filter))]
    # seccomp_data arch+nr. Any non-x86_64 syscall ABI is killed, preventing
    # int80/x32 number reinterpretation from bypassing the explicit deny list.
    rows = [(0x20,0,0,4), (0x15,1,0,0xc000003e), (0x06,0,0,0x80000000), (0x20,0,0,0)]
    denied = list(range(41,56)) + [109,112,288,299,307,425,426,427]
    # x32 bit: fail closed regardless of native syscall allow decision.
    rows += [(0x45,0,1,0x40000000), (0x06,0,0,0x80000000)]
    for number in denied:
        rows += [(0x15,0,1,number), (0x06,0,0,0x00050000 | errno.EPERM)]
    rows += [(0x06,0,0,0x7fff0000)]
    array = (Filter * len(rows))(*(Filter(*r) for r in rows))
    program = Program(len(rows),array)
    libc = ctypes.CDLL(None,use_errno=True)
    if libc.prctl(38,1,0,0,0) != 0:
        raise OSError(ctypes.get_errno(),'PR_SET_NO_NEW_PRIVS failed; abort before Engine')
    if libc.prctl(22,2,ctypes.byref(program),0,0) != 0:
        raise OSError(ctypes.get_errno(),'SECCOMP_FILTER failed; abort before Engine')
    return {'installed': True, 'scope':'worker+inherited descendants only; filesystem remains readable',
            'mechanism':'Linuxx86_64 seccompBPF permanent syscall socket/io_uring deny', 'deniedSyscalls':denied}
