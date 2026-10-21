1809D9305  mov [rbx+0CD0h],cl
1809D930B  mov [rbx+0C40h],cx
1809D9312  mov [rbx+0C44h],rcx
1809D9319  mov dword [rbx+0CD4h],0FFFFFFFFh
1809D9323  mov dword [rbx+0C5Ch],0FFFFFFFFh
1809D932D  mov [rbx+0C91h],cl
1809D9333  mov [rbx+0CB8h],rcx
1809D933A  mov [rbx+0CC0h],ecx
1809D9340  movss xmm0,[rel 1814318A0h]
1809D9348  movss [rbx+0C94h],xmm0
1809D9350  movss xmm1,[rel 1814318A4h]
1809D9358  movss [rbx+0C98h],xmm1
1809D9360  movss xmm0,[rel 1814318A8h]
1809D9368  movss [rbx+0C9Ch],xmm0
1809D9370  movss xmm1,[rel 1814318A0h]
1809D9378  movss [rbx+0CA0h],xmm1
1809D9380  movss xmm0,[rel 1814318A4h]
1809D9388  movss [rbx+0CA4h],xmm0
1809D9390  movss xmm1,[rel 1814318A8h]
1809D9398  movss [rbx+0CA8h],xmm1
1809D93A0  movss xmm0,[rel 181228820h]   ; Float32=3,4028235E+38
1809D93A8  movss [rbx+0CACh],xmm0
1809D93B0  movss xmm1,[rel 181228824h]   ; Float32=3,4028235E+38
1809D93B8  movss [rbx+0CB0h],xmm1
1809D93C0  movss xmm0,[rel 181228828h]   ; Float32=3,4028235E+38
1809D93C8  movss [rbx+0CB4h],xmm0
1809D93D0  mov [rbx+0CC4h],rcx
1809D93D7  mov [rbx+0CCCh],ecx
1809D93DD  mov [rbx+0C60h],cl
1809D93E3  mov [rbx+0C62h],cl
1809D93E9  add rsp,20h
1809D93ED  pop rbx
1809D93EE  ret
1809D93EF  int3
1809D93F0  mov [rsp+8],rbx
1809D93F5  push rdi
1809D93F6  sub rsp,20h
1809D93FA  mov ebx,edx
1809D93FC  mov rdi,rcx
1809D93FF  call 000000018029C150h
1809D9404  test bl,1
1809D9407  je short 00000001809D9416h
1809D9409  mov edx,0CD8h
1809D940E  mov rcx,rdi
1809D9411  call 0000000180C76EA0h
1809D9416  mov rax,rdi
1809D9419  mov rbx,[rsp+30h]
1809D941E  add rsp,20h
1809D9422  pop rdi
1809D9423  ret
1809D9424  int3
1809D9425  int3
1809D9426  int3
1809D9427  int3
1809D9428  int3
1809D9429  int3
1809D942A  int3
1809D942B  int3
1809D942C  int3
1809D942D  int3
1809D942E  int3
1809D942F  int3
1809D9430  mov [rsp+8],rbx
1809D9435  push rdi
1809D9436  sub rsp,20h
1809D943A  lea rdi,[rcx+0A60h]
1809D9441  mov rbx,rcx
1809D9444  mov rax,[rdi]
1809D9447  mov rcx,rdi
1809D944A  call qword [rax+30h]
1809D944D  test eax,eax
1809D944F  jg short 00000001809D9476h
1809D9451  mov rcx,rbx
1809D9454  call 00000001809DCCE0h
1809D9459  test eax,eax
1809D945B  jle short 00000001809D9476h
1809D945D  mov rcx,[rbx+10h]
1809D9461  call 0000000180BB5CF0h
1809D9466  mov rdx,rax
1809D9469  lea rcx,[rel 181065910h]
1809D9470  call qword [rel 180CC18F0h]
1809D9476  xor eax,eax
1809D9478  test rbx,rbx
1809D947B  cmove rdi,rax
1809D947F  call 0000000180A8D0A0h
1809D9484  mov rcx,rax
1809D9487  mov rdx,rdi
1809D948A  call 0000000180A8F510h
1809D948F  mov rcx,rbx
1809D9492  mov rbx,[rsp+30h]
1809D9497  add rsp,20h
1809D949B  pop rdi
1809D949C  jmp 0000000180767A30h
1809D94A1  int3
1809D94A2  int3
1809D94A3  int3
1809D94A4  int3
1809D94A5  int3
1809D94A6  int3
1809D94A7  int3
1809D94A8  int3
1809D94A9  int3
1809D94AA  int3
1809D94AB  int3
1809D94AC  int3
1809D94AD  int3
1809D94AE  int3
1809D94AF  int3
1809D94B0  mov rax,rsp
1809D94B3  push rbp
1809D94B4  push rbx
1809D94B5  push rdi
1809D94B6  lea rbp,[rax-5Fh]
1809D94BA  sub rsp,0E0h
1809D94C1  movsd xmm0,[rcx+318h]
1809D94C9  mov rdi,rcx
1809D94CC  movaps [rax-28h],xmm6
1809D94D0  movaps [rax-38h],xmm7
1809D94D4  movaps [rax-48h],xmm8
1809D94D9  movaps [rax-58h],xmm9
1809D94DE  movaps [rax-68h],xmm10
1809D94E3  mov [rax+18h],rsi
1809D94E7  mov rsi,rdx
1809D94EA  mov eax,[rcx+320h]
1809D94F0  lea rdx,[rel 1814318A0h]
1809D94F7  movsd [rbp-69h],xmm0
1809D94FC  lea rcx,[rbp-59h]
1809D9500  movss xmm7,[rbp-69h]
1809D9505  movss xmm10,[rbp-65h]
1809D950B  mov [rbp-61h],eax
1809D950E  movss xmm8,[rbp-61h]
1809D9514  movss [rbp-51h],xmm8
1809D951A  movss [rbp-59h],xmm7
1809D951F  movss [rbp-55h],xmm10
1809D9525  call 00000001800EC340h
1809D952A  movss xmm9,[rel 1810B9FB0h]   ; Float32=1
1809D9533  xorps xmm6,xmm6
1809D9536  test al,al
1809D9538  je short 00000001809D95B8h
1809D953A  mov rcx,[rdi+38h]
1809D953E  test rcx,rcx
1809D9541  je short 00000001809D9589h
1809D9543  mov rcx,[rcx+8]
1809D9547  mov rax,[rcx]
1809D954A  call qword [rax+48h]
1809D954D  test rax,rax
1809D9550  je short 00000001809D9589h
1809D9552  mov rbx,[rax+250h]
1809D9559  test rbx,rbx
1809D955C  je short 00000001809D9589h
1809D955E  mov rax,[rbx]
1809D9561  mov rcx,rbx
1809D9564  call qword [rax+60h]
1809D9567  test eax,eax
1809D9569  jle short 00000001809D9589h
1809D956B  mov rax,[rbx]
1809D956E  xor edx,edx
1809D9570  mov rcx,rbx
1809D9573  call qword [rax+70h]
1809D9576  test rax,rax
1809D9579  je short 00000001809D9589h
1809D957B  mov rdx,[rax]
1809D957E  mov rcx,rax
1809D9581  call qword [rdx+0C0h]
1809D9587  jmp short 00000001809D958Dh
1809D9589  movaps xmm0,xmm9
1809D958D  mov rax,[rel 18149A358h]
1809D9594  movss xmm8,[rax+54h]
1809D959A  movaps xmm7,xmm8
1809D959E  mulss xmm8,[rel 1810BAAECh]   ; Float32=-1
1809D95A7  mulss xmm7,xmm6
1809D95AB  mulss xmm8,xmm0
1809D95B0  mulss xmm7,xmm0
1809D95B4  movaps xmm10,xmm7
1809D95B8  mov rax,[rel 1814A1418h]
1809D95BF  lea rcx,[rbp-59h]
1809D95C3  mov qword [rbp-49h],0
1809D95CB  mov dword [rbp-41h],0BF800000h
1809D95D2  movss xmm0,[rax+54h]
1809D95D7  xorps xmm0,[rel 1810BB180h]   ; Packed128_Float32=-0
1809D95DE  movaps xmm2,xmm0
1809D95E1  movaps xmm1,xmm0
1809D95E4  mulss xmm0,[rsi+8]
1809D95E9  mulss xmm2,[rsi]
1809D95ED  mulss xmm1,[rsi+4]
1809D95F2  addss xmm0,xmm8
1809D95F7  addss xmm2,xmm7
1809D95FB  addss xmm1,xmm10
1809D9600  movss [rbp-61h],xmm0
1809D9605  mov eax,[rbp-61h]
1809D9608  movaps xmm0,xmm2
1809D960B  mov [rbp-51h],eax
1809D960E  unpcklps xmm0,xmm1
1809D9611  movsd [rbp-59h],xmm0
1809D9616  call 0000000180C09960h
1809D961B  ucomiss xmm0,xmm6
1809D961E  mov rsi,[rsp+110h]
1809D9626  lea rax,[rbp-59h]
1809D962A  jne short 00000001809D9630h
1809D962C  lea rax,[rbp-49h]
1809D9630  movsd xmm0,[rax]
1809D9634  lea rbx,[rdi+0CACh]
1809D963B  mov eax,[rax+8]
1809D963E  lea rdx,[rel 181228820h]
1809D9645  mov rcx,rbx
1809D9648  movaps [rsp+80h],xmm11
1809D9651  movaps [rsp+70h],xmm12
1809D9657  movsd [rbp-69h],xmm0
1809D965C  mov [rbp-61h],eax
1809D965F  call 00000001800EA270h
1809D9664  movss xmm10,[rbp-69h]
1809D966A  movss xmm11,[rbp-65h]
1809D9670  movss xmm12,[rbp-61h]
1809D9676  test al,al
1809D9678  je short 00000001809D96C9h
1809D967A  subss xmm10,[rbx]
1809D967F  subss xmm11,[rbx+4]
1809D9685  subss xmm12,[rbx+8]
1809D968B  mov rax,[rel 1814A1928h]
1809D9692  movss xmm2,[rax+54h]
1809D9697  mulss xmm10,xmm2
1809D969C  mulss xmm11,xmm2
1809D96A1  addss xmm10,[rbx]
1809D96A6  mulss xmm12,xmm2
1809D96AB  addss xmm11,[rbx+4]
1809D96B1  addss xmm12,[rbx+8]
1809D96B7  movss [rbp-69h],xmm10
1809D96BD  movss [rbp-65h],xmm11
1809D96C3  movss [rbp-61h],xmm12
1809D96C9  mov rdx,rbx
1809D96CC  lea rcx,[rbp-69h]
1809D96D0  call 00000001800EA270h
1809D96D5  test al,al
1809D96D7  je near 00000001809D9A43h
1809D96DD  cvtps2pd xmm1,xmm12
1809D96E1  cvtps2pd xmm0,xmm11
1809D96E5  movss [rbx],xmm10
1809D96EA  movss [rbx+4],xmm11
1809D96F0  movss [rbx+8],xmm12
1809D96F6  call qword [rel 180CC14F0h]
1809D96FC  movss xmm3,[rel 1810BA5C4h]   ; Float32=15
1809D9704  movaps xmm1,xmm10
1809D9708  mulsd xmm0,[rel 1810BA138h]   ; Float64=0,3183098861837907
1809D9710  addss xmm1,xmm9
1809D9715  movsd xmm4,[rel 1810BA0D0h]   ; Float64=0,15
1809D971D  xorps xmm2,xmm2
1809D9720  mulss xmm1,[rel 1810B9E7Ch]   ; Float32=0,5
1809D9728  cvtsd2ss xmm2,xmm0
1809D972C  mulss xmm1,xmm3
1809D9730  addss xmm2,xmm9
1809D9735  cvtps2pd xmm0,xmm1
1809D9738  mulss xmm2,[rel 1810B9E7Ch]   ; Float32=0,5
1809D9740  addsd xmm0,xmm4
1809D9744  cvtpd2ps xmm1,xmm0
1809D9748  cvttss2si ecx,xmm1
1809D974C  cmp ecx,80000000h
1809D9752  je short 00000001809D9772h
1809D9754  movd xmm0,ecx
1809D9758  cvtdq2ps xmm0,xmm0
1809D975B  ucomiss xmm0,xmm1
1809D975E  je short 00000001809D9772h
1809D9760  unpcklps xmm1,xmm1
1809D9763  movmskps eax,xmm1
1809D9766  and eax,1
1809D9769  sub ecx,eax
1809D976B  movd xmm1,ecx
1809D976F  cvtdq2ps xmm1,xmm1
1809D9772  cvttss2si ebx,xmm1
1809D9776  mulss xmm2,xmm3
1809D977A  shl bl,4
1809D977D  cvtps2pd xmm0,xmm2
1809D9780  addsd xmm0,xmm4
1809D9784  cvtpd2ps xmm1,xmm0
1809D9788  cvttss2si ecx,xmm1
1809D978C  cmp ecx,80000000h
1809D9792  je short 00000001809D97B2h
1809D9794  movd xmm0,ecx
1809D9798  cvtdq2ps xmm0,xmm0
1809D979B  ucomiss xmm0,xmm1
1809D979E  je short 00000001809D97B2h
1809D97A0  unpcklps xmm1,xmm1
1809D97A3  movmskps eax,xmm1
1809D97A6  and eax,1
1809D97A9  sub ecx,eax
1809D97AB  movd xmm1,ecx
1809D97AF  cvtdq2ps xmm1,xmm1
1809D97B2  cvttss2si eax,xmm1
1809D97B6  and al,0Fh
1809D97B8  or bl,al
1809D97BA  cmp [rdi+467h],bl
1809D97C0  je short 00000001809D97E3h
1809D97C2  mov rax,[rdi]
1809D97C5  mov r9d,0FFFFh
1809D97CB  mov rcx,rdi
1809D97CE  or r8d,0FFFFFFFFh
1809D97D2  mov edx,464h
1809D97D7  call qword [rax+0A0h]
1809D97DD  mov [rdi+467h],bl
1809D97E3  mov rax,[rel 1814A19B8h]
1809D97EA  cmp dword [rax+58h],0
1809D97EE  je near 00000001809D9A43h
1809D97F4  movss xmm0,[rel 1810B9D74h]   ; Float32=0,0625
1809D97FC  movzx ecx,bl
1809D97FF  and cl,0Fh
1809D9802  shr bl,4
1809D9805  movaps [rsp+60h],xmm13
1809D980B  test bl,bl
1809D980D  je short 00000001809D9824h
1809D980F  movzx eax,bl
1809D9812  inc eax
1809D9814  movd xmm13,eax
1809D9819  cvtdq2ps xmm13,xmm13
1809D981D  mulss xmm13,xmm0
1809D9822  jmp short 00000001809D9828h
1809D9824  movaps xmm13,xmm6
1809D9828  test cl,cl
1809D982A  je short 00000001809D983Ch
1809D982C  movzx eax,cl
1809D982F  inc eax
1809D9831  movd xmm6,eax
1809D9835  cvtdq2ps xmm6,xmm6
1809D9838  mulss xmm6,xmm0
1809D983C  mulss xmm6,[rel 1810BA2F0h]   ; Float32=2
1809D9844  lea r8,[rbp+73h]
1809D9848  lea rdx,[rbp+6Fh]
1809D984C  mulss xmm13,[rel 1810BA2F0h]   ; Float32=2
1809D9855  subss xmm6,xmm9
1809D985A  subss xmm13,xmm9
1809D985F  cvtps2pd xmm0,xmm6
1809D9862  mulsd xmm0,[rel 1810BA338h]   ; Float64=3,141592653589793
1809D986A  cvtpd2ps xmm0,xmm0
1809D986E  call qword [rel 180CC18E0h]
1809D9874  movss xmm1,[rbp+6Fh]
1809D9879  movss xmm3,[rel 1810BA874h]   ; Float32=1000
1809D9881  mulss xmm1,xmm3
1809D9885  cvttss2si ecx,xmm1
1809D9889  cmp ecx,80000000h
1809D988F  je short 00000001809D98AFh
1809D9891  movd xmm0,ecx
1809D9895  cvtdq2ps xmm0,xmm0
1809D9898  ucomiss xmm0,xmm1
1809D989B  je short 00000001809D98AFh
1809D989D  unpcklps xmm1,xmm1
1809D98A0  movmskps eax,xmm1
1809D98A3  and eax,1
1809D98A6  sub ecx,eax
1809D98A8  movd xmm1,ecx
1809D98AC  cvtdq2ps xmm1,xmm1
1809D98AF  movss xmm2,[rbp+73h]
1809D98B4  movss xmm4,[rel 1810B9C70h]   ; Float32=0,001
1809D98BC  mulss xmm2,xmm3
1809D98C0  mulss xmm1,xmm4
1809D98C4  cvttss2si ecx,xmm2
1809D98C8  movss [rbp+6Fh],xmm1
1809D98CD  cmp ecx,80000000h
1809D98D3  je short 00000001809D98F3h
1809D98D5  movd xmm0,ecx
1809D98D9  cvtdq2ps xmm0,xmm0
1809D98DC  ucomiss xmm0,xmm2
1809D98DF  je short 00000001809D98F3h
1809D98E1  unpcklps xmm2,xmm2
1809D98E4  movmskps eax,xmm2
1809D98E7  and eax,1
1809D98EA  sub ecx,eax
1809D98EC  movd xmm2,ecx
1809D98F0  cvtdq2ps xmm2,xmm2
1809D98F3  movss xmm7,[rel 1810BA598h]   ; Float32=10
1809D98FB  movaps xmm0,xmm13
1809D98FF  mov rbx,[rdi+1A0h]
1809D9906  mulss xmm0,xmm13
1809D990B  mov rcx,rbx
1809D990E  mulss xmm2,xmm4
1809D9912  subss xmm9,xmm0
1809D9917  mulss xmm10,xmm7
1809D991C  movss [rbp+73h],xmm2
1809D9921  mulss xmm11,xmm7
1809D9926  sqrtss xmm9,xmm9
1809D992B  mulss xmm12,xmm7
1809D9930  movaps xmm8,xmm9
1809D9934  mulss xmm9,xmm2
1809D9939  mulss xmm8,xmm1
1809D993E  call 00000001800E9AA0h
1809D9943  addss xmm10,[rbx+108h]
1809D994C  movss [rbp-49h],xmm10
1809D9952  addss xmm11,[rbx+10Ch]
1809D995B  movss [rbp-45h],xmm11
1809D9961  addss xmm12,[rbx+110h]
1809D996A  mov rbx,[rdi+1A0h]
1809D9971  mov rcx,rbx
1809D9974  movss [rbp-41h],xmm12
1809D997A  call 00000001800E9AA0h
1809D997F  mov rcx,[rel 1814E60A8h]
1809D9986  lea rdx,[rbx+108h]
1809D998D  movsd xmm6,[rel 1810BA0A8h]   ; Float64=0,10000000149011612
1809D9995  lea r9,[rbp+67h]
1809D9999  movsd [rsp+28h],xmm6
1809D999F  lea r8,[rbp-49h]
1809D99A3  mov dword [rbp+67h],640000FFh
1809D99AA  mov rax,[rcx]
1809D99AD  mov byte [rsp+20h],1
1809D99B2  call qword [rax+68h]
1809D99B5  mov rbx,[rdi+1A0h]
1809D99BC  mov rcx,rbx
1809D99BF  mulss xmm13,xmm7
1809D99C4  mulss xmm8,xmm7
1809D99C9  mulss xmm9,xmm7
1809D99CE  call 00000001800E9AA0h
1809D99D3  addss xmm13,[rbx+108h]
1809D99DC  movss [rbp-49h],xmm13
1809D99E2  addss xmm8,[rbx+10Ch]
1809D99EB  movss [rbp-45h],xmm8
1809D99F1  addss xmm9,[rbx+110h]
1809D99FA  mov rbx,[rdi+1A0h]
1809D9A01  mov rcx,rbx
1809D9A04  movss [rbp-41h],xmm9
1809D9A0A  call 00000001800E9AA0h
1809D9A0F  mov rcx,[rel 1814E60A8h]
1809D9A16  lea rdx,[rbx+108h]
1809D9A1D  movsd [rsp+28h],xmm6
1809D9A23  lea r9,[rbp+67h]
1809D9A27  lea r8,[rbp-49h]
1809D9A2B  mov dword [rbp+67h],64FF0000h
1809D9A32  mov byte [rsp+20h],1
1809D9A37  mov rax,[rcx]
1809D9A3A  call qword [rax+68h]
1809D9A3D  movaps xmm13,[rsp+60h]
1809D9A43  movaps xmm12,[rsp+70h]
1809D9A49  lea r11,[rsp+0E0h]
1809D9A51  movaps xmm6,[r11-10h]
1809D9A56  movaps xmm7,[r11-20h]
1809D9A5B  movaps xmm8,[r11-30h]
1809D9A60  movaps xmm9,[r11-40h]
1809D9A65  movaps xmm10,[r11-50h]
1809D9A6A  movaps xmm11,[rsp+80h]
1809D9A73  mov rsp,r11
1809D9A76  pop rdi
1809D9A77  pop rbx
1809D9A78  pop rbp
1809D9A79  ret
1809D9A7A  int3
1809D9A7B  int3
1809D9A7C  int3
1809D9A7D  int3
1809D9A7E  int3
1809D9A7F  int3
1809D9A80  push rbp
1809D9A82  push rdi
1809D9A83  push r14
1809D9A85  push r15
1809D9A87  mov rbp,rsp
1809D9A8A  sub rsp,78h
1809D9A8E  mov rax,[rcx+10h]
1809D9A92  mov rdi,rcx
1809D9A95  mov r15,rdx
1809D9A98  lea rcx,[rel 180D0874Ah]
1809D9A9F  lea rdx,[rel 180DED8B8h]   ; "prop_physics_override"
1809D9AA6  mov r8,[rax+20h]
1809D9AAA  test r8,r8
1809D9AAD  cmovne rcx,r8
1809D9AB1  call qword [rel 180CC1468h]
1809D9AB7  mov rcx,[rdi+10h]
1809D9ABB  test eax,eax
1809D9ABD  sete al
1809D9AC0  mov [rdi+0C41h],al
1809D9AC6  call 00000001800EC2C0h
1809D9ACB  xor r9d,r9d
1809D9ACE  mov edx,1ADDA1Dh
1809D9AD3  lea rcx,[rax+8]
1809D9AD7  lea r8d,[r9+1]
1809D9ADB  call 00000001801C5A50h
1809D9AE0  mov rdx,r15
1809D9AE3  mov rcx,rdi
1809D9AE6  call 0000000180782240h
1809D9AEB  xor r14d,r14d
1809D9AEE  cmp [rdi+0C41h],r14b
1809D9AF5  je short 00000001809D9B34h
1809D9AF7  mov edx,9AA70A58h
1809D9AFC  mov rcx,r15
1809D9AFF  call 0000000180BCD760h
1809D9B04  test al,al
1809D9B06  je short 00000001809D9B34h
1809D9B08  mov edx,9AA70A58h
1809D9B0D  mov [rbp+28h],r14d
1809D9B11  mov rcx,r15
1809D9B14  call 00000001800E9C90h
1809D9B19  mov rcx,rax
1809D9B1C  lea rdx,[rbp+28h]
1809D9B20  call 00000001801C2D50h
1809D9B25  mov rax,[rdi]
1809D9B28  mov rcx,rdi
1809D9B2B  mov edx,[rbp+28h]
1809D9B2E  call qword [rax+568h]
1809D9B34  mov rax,[rdi+10h]
1809D9B38  mov byte [rdi+444h],1
1809D9B3F  mov ecx,[rax+30h]
1809D9B42  shr ecx,9
1809D9B45  test cl,1
1809D9B48  jne near 00000001809D9FD2h
1809D9B4E  mov [rsp+0A8h],rbx
1809D9B56  mov [rsp+0B0h],rsi
1809D9B5E  lea rsi,[rdi+4C0h]
1809D9B65  cmp byte [rsi+56h],18h
1809D9B69  mov [rsp+70h],r12
1809D9B6E  mov r12d,0FFFFh
1809D9B74  je short 00000001809D9B97h
1809D9B76  mov rax,[rsi]
1809D9B79  or r8d,0FFFFFFFFh
1809D9B7D  mov rcx,rsi
1809D9B80  movzx r9d,r12w
1809D9B84  lea edx,[r8+57h]
1809D9B88  call qword [rax+10h]
1809D9B8B  mov rcx,rsi
1809D9B8E  mov byte [rsi+56h],18h
1809D9B92  call 0000000180866B20h
1809D9B97  cmp [rdi+0C58h],r14b
1809D9B9E  je short 00000001809D9BB0h
1809D9BA0  movss xmm1,[rel 1810BAA4Ch]   ; Float32=3,4028235E+38
1809D9BA8  mov rcx,rdi
1809D9BAB  call 00000001800EB3F0h
1809D9BB0  mov eax,[rdi+24Ch]
1809D9BB6  shr eax,2
1809D9BB9  test al,1
1809D9BBB  jne short 00000001809D9C07h
1809D9BBD  mov rax,[rdi+860h]
1809D9BC4  lea rcx,[rdi+860h]
1809D9BCB  mov edx,9
1809D9BD0  call qword [rax+58h]
1809D9BD3  test al,al
1809D9BD5  jne short 00000001809D9C07h
1809D9BD7  mov rax,[rdi+860h]
1809D9BDE  lea rcx,[rdi+860h]
1809D9BE5  mov edx,13h
1809D9BEA  call qword [rax+58h]
1809D9BED  test al,al
1809D9BEF  je near 00000001809D9CA2h
1809D9BF5  mov edx,0Eh
1809D9BFA  mov rcx,rdi
1809D9BFD  call 00000001800EB150h
