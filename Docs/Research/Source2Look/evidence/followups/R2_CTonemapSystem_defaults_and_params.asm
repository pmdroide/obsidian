18010BE00  test rax,rax
18010BE03  je short 000000018010BE7Ch
18010BE05  xor r8d,r8d
18010BE08  lea rdx,[rel 180267F80h]   ; "Engine2"
18010BE0F  mov rcx,rax
18010BE12  call 00000001801E85F0h
18010BE17  test rax,rax
18010BE1A  je short 000000018010BE7Ch
18010BE1C  xor r8d,r8d
18010BE1F  lea rdx,[rel 180267F68h]   ; "RenderingPipeline"
18010BE26  mov rcx,rax
18010BE29  call 00000001801E85F0h
18010BE2E  mov rdi,rax
18010BE31  test rax,rax
18010BE34  je short 000000018010BE7Fh
18010BE36  xor r8d,r8d
18010BE39  lea rdx,[rel 180267F38h]   ; "Tonemapping_UseAlphaChannelForAutoExposure"
18010BE40  mov rcx,rax
18010BE43  call 00000001801E8950h
18010BE48  test eax,eax
18010BE4A  je short 000000018010BE53h
18010BE4C  mov byte [rbx+338h],1
18010BE53  xor r8d,r8d
18010BE56  lea rdx,[rel 180267F18h]   ; "Tonemapping_UseLogLuminance"
18010BE5D  mov rcx,rdi
18010BE60  call 00000001801E8950h
18010BE65  test eax,eax
18010BE67  je short 000000018010BE7Fh
18010BE69  mov byte [rbx+339h],1
18010BE70  mov dword [rbx+33Ch],10h
18010BE7A  jmp short 000000018010BE7Fh
18010BE7C  mov rdi,rsi
18010BE7F  mov dword [rbx+28Ch],3F800000h
18010BE89  mov dword [rbx+290h],3F800000h
18010BE93  mov qword [rbx+294h],3F800000h
18010BE9E  mov qword [rbx+29Ch],3F800000h
18010BEA9  mov dword [rbx+2D8h],3E800000h
18010BEB3  mov dword [rbx+2DCh],41000000h
18010BEBD  mov dword [rbx+2E4h],42700000h
18010BEC7  mov dword [rbx+2E8h],40A00000h
18010BED1  mov dword [rbx+2ECh],40400000h
18010BEDB  mov dword [rbx+2F0h],40000000h
18010BEE5  mov dword [rbx+2F4h],40800000h
18010BEEF  test rdi,rdi
18010BEF2  je short 000000018010BF1Bh
18010BEF4  xor r8d,r8d
18010BEF7  lea rdx,[rel 180268068h]   ; "TonemappingVRForward"
18010BEFE  mov rcx,rdi
18010BF01  call 00000001801E8950h
18010BF06  test eax,eax
18010BF08  je short 000000018010BF1Bh
18010BF0A  mov dword [rbx+2F0h],3E99999Ah
18010BF14  mov byte [rbx+33Ah],1
18010BF1B  mov rcx,rbx
18010BF1E  call 000000018010BF90h
18010BF23  call 000000018010C490h
18010BF28  mov rsi,[rsp+38h]
18010BF2D  mov rax,rbx
18010BF30  mov rbx,[rsp+30h]
18010BF35  add rsp,20h
18010BF39  pop rdi
18010BF3A  ret
18010BF40  movss xmm0,[rel 180269804h]   ; Float32=1
18010BF48  ret
18010BF50  test rdx,rdx
18010BF53  jne short 000000018010BF76h
18010BF55  movups xmm0,[rel 180269D90h]   ; Packed128_Float32=-1
18010BF5C  movups xmm1,[rel 180269DD0h]   ; Packed128_Float32=-1
18010BF63  movups [rcx+2F8h],xmm0
18010BF6A  movups [rcx+308h],xmm1
18010BF71  jmp 000000018010BF90h
18010BF76  movups xmm0,[rdx]
18010BF79  movups [rcx+2F8h],xmm0
18010BF80  movups xmm1,[rdx+10h]
18010BF84  movups [rcx+308h],xmm1
18010BF8B  jmp 000000018010BF90h
18010BF90  mov rax,[rel 1802D31C8h]
18010BF97  xorps xmm0,xmm0
18010BF9A  movss xmm2,[rel 180269970h]   ; Float32=-1
18010BFA2  cmp dword [rax+58h],0
18010BFA6  je short 000000018010BFC1h
18010BFA8  mov dword [rcx+318h],0
18010BFB2  mov dword [rcx+31Ch],42C80000h
18010BFBC  jmp 000000018010C04Eh
18010BFC1  mov rax,[rel 1802D2F88h]
18010BFC8  movss xmm3,[rcx+2D8h]
18010BFD0  movss xmm4,[rcx+2F8h]
18010BFD8  movss xmm1,[rax+54h]
18010BFDD  comiss xmm1,xmm0
18010BFE0  jae short 000000018010BFF9h
18010BFE2  comiss xmm4,xmm0
18010BFE5  jb short 000000018010BFECh
18010BFE7  movaps xmm1,xmm4
18010BFEA  jmp short 000000018010BFF9h
18010BFEC  comiss xmm3,xmm0
18010BFEF  jb short 000000018010BFF6h
18010BFF1  movaps xmm1,xmm3
18010BFF4  jmp short 000000018010BFF9h
18010BFF6  movaps xmm1,xmm2
18010BFF9  movss xmm4,[rcx+2DCh]
18010C001  movss xmm5,[rcx+2FCh]
18010C009  movss [rcx+318h],xmm1
18010C011  mov rax,[rel 1802D32E8h]
18010C018  movss xmm3,[rax+54h]
18010C01D  comiss xmm3,xmm0
18010C020  jae short 000000018010C039h
18010C022  comiss xmm5,xmm0
18010C025  jb short 000000018010C02Ch
18010C027  movaps xmm3,xmm5
18010C02A  jmp short 000000018010C039h
18010C02C  comiss xmm4,xmm0
18010C02F  jb short 000000018010C036h
18010C031  movaps xmm3,xmm4
18010C034  jmp short 000000018010C039h
18010C036  movaps xmm3,xmm2
18010C039  comiss xmm3,xmm1
18010C03C  movss [rcx+31Ch],xmm3
18010C044  jae short 000000018010C04Eh
18010C046  movss [rcx+31Ch],xmm1
18010C04E  mov rax,[rel 1802D3018h]
18010C055  movss xmm3,[rcx+2E4h]
18010C05D  movss xmm4,[rcx+304h]
18010C065  movss xmm1,[rax+54h]
18010C06A  comiss xmm1,xmm0
18010C06D  jae short 000000018010C086h
18010C06F  comiss xmm4,xmm0
18010C072  jb short 000000018010C079h
18010C074  movaps xmm1,xmm4
18010C077  jmp short 000000018010C086h
18010C079  comiss xmm3,xmm0
18010C07C  jb short 000000018010C083h
18010C07E  movaps xmm1,xmm3
18010C081  jmp short 000000018010C086h
18010C083  movaps xmm1,xmm2
18010C086  minss xmm1,[rel 1802698B8h]   ; Float32=92,25
18010C08E  movss xmm4,[rcx+2E8h]
18010C096  movss xmm5,[rcx+308h]
18010C09E  movss [rcx+324h],xmm1
18010C0A6  mov rax,[rel 1802D3498h]
18010C0AD  movss xmm3,[rax+54h]
18010C0B2  comiss xmm3,xmm0
18010C0B5  jae short 000000018010C0CEh
18010C0B7  comiss xmm5,xmm0
18010C0BA  jb short 000000018010C0C1h
18010C0BC  movaps xmm3,xmm5
18010C0BF  jmp short 000000018010C0CEh
18010C0C1  comiss xmm4,xmm0
18010C0C4  jb short 000000018010C0CBh
18010C0C6  movaps xmm3,xmm4
18010C0C9  jmp short 000000018010C0CEh
18010C0CB  movaps xmm3,xmm2
18010C0CE  movss xmm4,[rcx+2ECh]
18010C0D6  movss xmm5,[rcx+30Ch]
18010C0DE  movss [rcx+328h],xmm3
18010C0E6  mov rax,[rel 1802D3138h]
18010C0ED  movss xmm3,[rax+54h]
18010C0F2  comiss xmm3,xmm0
18010C0F5  jae short 000000018010C10Eh
18010C0F7  comiss xmm5,xmm0
18010C0FA  jb short 000000018010C101h
18010C0FC  movaps xmm3,xmm5
18010C0FF  jmp short 000000018010C10Eh
18010C101  comiss xmm4,xmm0
18010C104  jb short 000000018010C10Bh
18010C106  movaps xmm3,xmm4
18010C109  jmp short 000000018010C10Eh
18010C10B  movaps xmm3,xmm2
18010C10E  movss xmm4,[rcx+310h]
18010C116  minss xmm1,xmm3
18010C11A  movss [rcx+32Ch],xmm1
18010C122  mov rax,[rel 1802D3258h]
18010C129  movss xmm1,[rcx+2F0h]
18010C131  movss xmm3,[rax+54h]
18010C136  comiss xmm3,xmm0
18010C139  jae short 000000018010C152h
18010C13B  comiss xmm4,xmm0
18010C13E  jb short 000000018010C145h
18010C140  movaps xmm3,xmm4
18010C143  jmp short 000000018010C152h
18010C145  comiss xmm1,xmm0
18010C148  jb short 000000018010C14Fh
18010C14A  movaps xmm3,xmm1
18010C14D  jmp short 000000018010C152h
18010C14F  movaps xmm3,xmm2
18010C152  movss xmm4,[rcx+314h]
18010C15A  movss [rcx+330h],xmm3
18010C162  mov rax,[rel 1802D2E68h]
18010C169  movss xmm3,[rcx+2F4h]
18010C171  movss xmm1,[rax+54h]
18010C176  comiss xmm1,xmm0
18010C179  jae short 000000018010C192h
18010C17B  comiss xmm4,xmm0
18010C17E  jb short 000000018010C185h
18010C180  movaps xmm1,xmm4
18010C183  jmp short 000000018010C192h
18010C185  comiss xmm3,xmm0
18010C188  jb short 000000018010C18Fh
18010C18A  movaps xmm1,xmm3
18010C18D  jmp short 000000018010C192h
18010C18F  movaps xmm1,xmm2
18010C192  movss xmm4,[rcx+300h]
18010C19A  comiss xmm4,xmm0
18010C19D  movss xmm3,[rcx+2E0h]
18010C1A5  movss [rcx+334h],xmm1
18010C1AD  jb short 000000018010C1B8h
18010C1AF  movss [rcx+320h],xmm4
18010C1B7  ret
18010C1B8  comiss xmm3,xmm0
18010C1BB  jb short 000000018010C1C6h
18010C1BD  movss [rcx+320h],xmm3
18010C1C5  ret
18010C1C6  movss [rcx+320h],xmm2
18010C1CE  ret
18010C1D0  movss xmm0,[rcx+294h]
18010C1D8  ret
18010C1E0  movss xmm0,[rcx+318h]
18010C1E8  ret
18010C1F0  movss xmm0,[rcx+31Ch]
18010C1F8  ret
