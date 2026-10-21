===== 0x1802C9870+38
   18008EDE5  cmovg r14d,eax
   18008EDE9  xor eax,eax
   18008EDEB  mov edi,r12d
   18008EDEE  mov [rsp+60h],rax
   18008EDF3  mov [rsp+68h],rax
   18008EDF8  test rdx,rdx
   18008EDFB  je short 000000018008EE5Bh
   18008EDFD  mov eax,r12d
   18008EE00  test r14d,r14d
   18008EE03  je short 000000018008EE5Bh
   18008EE05  nop word [rax+rax]
   18008EE10  mov ecx,eax
   18008EE12  movss xmm0,[rdx+rcx*4+54h]
   18008EE18  comiss xmm0,xmm7
   18008EE1B  jbe short 000000018008EE2Ch
   18008EE1D  inc edi
   18008EE1F  movss [rsp+rcx*4+60h],xmm0
   18008EE25  inc eax
   18008EE27  cmp eax,r14d
   18008EE2A  jb short 000000018008EE10h
   18008EE2C  test edi,edi
   18008EE2E  je short 000000018008EE5Bh
   18008EE30  mov r8d,edi
   18008EE33  lea rax,[rel 18008F470h]
   18008EE3A  lea rdx,[rsp+60h]
   18008EE3F  mov [rbp+130h],rax
   18008EE46  lea r9,[rbp+130h]
   18008EE4D  lea rcx,[rsp+60h]
   18008EE52  lea rdx,[rdx+r8*4]
   18008EE56  call 000000018008F550h
>> 18008EE5B  mov rax,[rel 1802C98A8h]
   18008EE62  movss xmm0,[rax+54h]
   18008EE67  comiss xmm0,xmm7
   18008EE6A  ja short 000000018008EE72h
   18008EE6C  movss xmm0,[rsp+60h]
   18008EE72  mov rax,[rel 1802C9C08h]
   18008EE79  movss [rsp+60h],xmm0
   18008EE7F  movss xmm1,[rax+54h]
   18008EE84  comiss xmm1,xmm7
   18008EE87  ja short 000000018008EE8Fh
   18008EE89  movss xmm1,[rsp+64h]
   18008EE8F  mov rax,[rel 1802C9ED8h]
   18008EE96  movss [rsp+64h],xmm1
   18008EE9C  movss xmm0,[rax+54h]
   18008EEA1  comiss xmm0,xmm7
   18008EEA4  ja short 000000018008EEACh
   18008EEA6  movss xmm0,[rsp+68h]
   18008EEAC  mov rax,[rel 1802C9E48h]
   18008EEB3  movss [rsp+68h],xmm0
   18008EEB9  movss xmm1,[rax+54h]
   18008EEBE  comiss xmm1,xmm7
   18008EEC1  ja short 000000018008EEC9h
   18008EEC3  movss xmm1,[rsp+6Ch]
   18008EEC9  mov rax,[rsi+18h]
   18008EECD  movss xmm8,[rel 180269790h]   ; Float32=0,125
   18008EED6  movss [rsp+6Ch],xmm1
   18008EEDC  mov [rbp+130h],rax
   18008EEE3  movss xmm0,[rax+20h]
   18008EEE8  comiss xmm0,xmm8
   18008EEEC  jb short 000000018008EEF2h
   18008EEEE  movaps xmm8,xmm0
   18008EEF2  movss xmm1,[rax+24h]
   18008EEF7  mov rax,[rel 1802D1120h]
   18008EEFE  test byte [rax+8],8
   18008EF02  je short 000000018008EF1Eh
   18008EF04  comiss xmm8,xmm14
   18008EF08  jb short 000000018008EF0Eh
   18008EF0A  movaps xmm8,xmm14
   18008EF0E  movss xmm0,[rel 180269918h]   ; Float32=10000
   18008EF16  comiss xmm1,xmm0
   18008EF19  ja short 000000018008EF1Eh
   18008EF1B  movaps xmm1,xmm0
   18008EF1E  cmp edi,r14d
   18008EF21  jae near 000000018008F062h
   18008EF27  mov rax,[rel 1802C9F68h]
===== 0x1802C9F30+38
   18008EEA1  comiss xmm0,xmm7
   18008EEA4  ja short 000000018008EEACh
   18008EEA6  movss xmm0,[rsp+68h]
   18008EEAC  mov rax,[rel 1802C9E48h]
   18008EEB3  movss [rsp+68h],xmm0
   18008EEB9  movss xmm1,[rax+54h]
   18008EEBE  comiss xmm1,xmm7
   18008EEC1  ja short 000000018008EEC9h
   18008EEC3  movss xmm1,[rsp+6Ch]
   18008EEC9  mov rax,[rsi+18h]
   18008EECD  movss xmm8,[rel 180269790h]   ; Float32=0,125
   18008EED6  movss [rsp+6Ch],xmm1
   18008EEDC  mov [rbp+130h],rax
   18008EEE3  movss xmm0,[rax+20h]
   18008EEE8  comiss xmm0,xmm8
   18008EEEC  jb short 000000018008EEF2h
   18008EEEE  movaps xmm8,xmm0
   18008EEF2  movss xmm1,[rax+24h]
   18008EEF7  mov rax,[rel 1802D1120h]
   18008EEFE  test byte [rax+8],8
   18008EF02  je short 000000018008EF1Eh
   18008EF04  comiss xmm8,xmm14
   18008EF08  jb short 000000018008EF0Eh
   18008EF0A  movaps xmm8,xmm14
   18008EF0E  movss xmm0,[rel 180269918h]   ; Float32=10000
   18008EF16  comiss xmm1,xmm0
   18008EF19  ja short 000000018008EF1Eh
   18008EF1B  movaps xmm1,xmm0
   18008EF1E  cmp edi,r14d
   18008EF21  jae near 000000018008F062h
>> 18008EF27  mov rax,[rel 1802C9F68h]
   18008EF2E  movaps [rsp+1A0h],xmm10
   18008EF37  movss xmm10,[rel 180269900h]   ; Float32=1000
   18008EF40  movaps [rsp+190h],xmm11
   18008EF49  movss xmm0,[rax+54h]
   18008EF4E  comiss xmm0,xmm7
   18008EF51  movaps [rsp+180h],xmm12
   18008EF5A  movaps [rsp+170h],xmm13
   18008EF63  jbe short 000000018008EF69h
   18008EF65  movaps xmm10,xmm0
   18008EF69  movss xmm13,[rel 180269784h]   ; Float32=0,1
   18008EF72  minss xmm10,xmm1
   18008EF77  mov rsi,[rel 1802C9B78h]
   18008EF7E  lea r15,[rsp+60h]
   18008EF83  movd xmm0,r14d
   18008EF88  movaps xmm11,xmm14
   18008EF8C  cvtdq2ps xmm0,xmm0
   18008EF8F  mov eax,edi
   18008EF91  movaps xmm12,xmm10
   18008EF95  subss xmm10,xmm8
   18008EF9A  lea r15,[r15+rax*4]
   18008EF9E  divss xmm11,xmm0
   18008EFA3  divss xmm12,xmm8
   18008EFA8  nop dword [rax+rax]
   18008EFB0  lea r13d,[rdi+1]
   18008EFB4  movaps xmm0,xmm12
   18008EFB8  movd xmm6,r13d
   18008EFBD  cvtdq2ps xmm6,xmm6
   18008EFC0  mulss xmm6,xmm11
   18008EFC5  movaps xmm1,xmm6
   18008EFC8  call 0000000180207E30h
   18008EFCD  movss xmm2,[rsi+54h]
   18008EFD2  movaps xmm3,xmm10
   18008EFD6  comiss xmm2,xmm7
   18008EFD9  mulss xmm3,xmm6
   18008EFDD  mulss xmm0,xmm8
   18008EFE2  addss xmm3,xmm8
   18008EFE7  jae short 000000018008EFEEh
   18008EFE9  movaps xmm2,xmm7
   18008EFEC  jmp short 000000018008EFF3h
   18008EFEE  minss xmm2,xmm14
   18008EFF3  movaps xmm1,xmm14
   18008EFF7  subss xmm1,xmm2
   18008EFFB  mulss xmm2,xmm0
   18008EFFF  mulss xmm1,xmm3
===== 0x1802C9B40+38
   18008EEDC  mov [rbp+130h],rax
   18008EEE3  movss xmm0,[rax+20h]
   18008EEE8  comiss xmm0,xmm8
   18008EEEC  jb short 000000018008EEF2h
   18008EEEE  movaps xmm8,xmm0
   18008EEF2  movss xmm1,[rax+24h]
   18008EEF7  mov rax,[rel 1802D1120h]
   18008EEFE  test byte [rax+8],8
   18008EF02  je short 000000018008EF1Eh
   18008EF04  comiss xmm8,xmm14
   18008EF08  jb short 000000018008EF0Eh
   18008EF0A  movaps xmm8,xmm14
   18008EF0E  movss xmm0,[rel 180269918h]   ; Float32=10000
   18008EF16  comiss xmm1,xmm0
   18008EF19  ja short 000000018008EF1Eh
   18008EF1B  movaps xmm1,xmm0
   18008EF1E  cmp edi,r14d
   18008EF21  jae near 000000018008F062h
   18008EF27  mov rax,[rel 1802C9F68h]
   18008EF2E  movaps [rsp+1A0h],xmm10
   18008EF37  movss xmm10,[rel 180269900h]   ; Float32=1000
   18008EF40  movaps [rsp+190h],xmm11
   18008EF49  movss xmm0,[rax+54h]
   18008EF4E  comiss xmm0,xmm7
   18008EF51  movaps [rsp+180h],xmm12
   18008EF5A  movaps [rsp+170h],xmm13
   18008EF63  jbe short 000000018008EF69h
   18008EF65  movaps xmm10,xmm0
   18008EF69  movss xmm13,[rel 180269784h]   ; Float32=0,1
   18008EF72  minss xmm10,xmm1
>> 18008EF77  mov rsi,[rel 1802C9B78h]
   18008EF7E  lea r15,[rsp+60h]
   18008EF83  movd xmm0,r14d
   18008EF88  movaps xmm11,xmm14
   18008EF8C  cvtdq2ps xmm0,xmm0
   18008EF8F  mov eax,edi
   18008EF91  movaps xmm12,xmm10
   18008EF95  subss xmm10,xmm8
   18008EF9A  lea r15,[r15+rax*4]
   18008EF9E  divss xmm11,xmm0
   18008EFA3  divss xmm12,xmm8
   18008EFA8  nop dword [rax+rax]
   18008EFB0  lea r13d,[rdi+1]
   18008EFB4  movaps xmm0,xmm12
   18008EFB8  movd xmm6,r13d
   18008EFBD  cvtdq2ps xmm6,xmm6
   18008EFC0  mulss xmm6,xmm11
   18008EFC5  movaps xmm1,xmm6
   18008EFC8  call 0000000180207E30h
   18008EFCD  movss xmm2,[rsi+54h]
   18008EFD2  movaps xmm3,xmm10
   18008EFD6  comiss xmm2,xmm7
   18008EFD9  mulss xmm3,xmm6
   18008EFDD  mulss xmm0,xmm8
   18008EFE2  addss xmm3,xmm8
   18008EFE7  jae short 000000018008EFEEh
   18008EFE9  movaps xmm2,xmm7
   18008EFEC  jmp short 000000018008EFF3h
   18008EFEE  minss xmm2,xmm14
   18008EFF3  movaps xmm1,xmm14
   18008EFF7  subss xmm1,xmm2
   18008EFFB  mulss xmm2,xmm0
   18008EFFF  mulss xmm1,xmm3
   18008F003  addss xmm1,xmm2
   18008F007  addss xmm1,xmm13
   18008F00C  movss [r15],xmm1
   18008F011  test edi,edi
   18008F013  je short 000000018008F027h
   18008F015  lea eax,[rdi-1]
   18008F018  movss xmm0,[rsp+rax*4+60h]
   18008F01E  maxss xmm0,xmm1
   18008F022  movss [r15],xmm0
   18008F027  add r15,4
   18008F02B  mov edi,r13d
   18008F02E  cmp r13d,r14d
