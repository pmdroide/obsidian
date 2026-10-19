===== 0x1802C8250+38
   18004C601  movups [rcx-80h],xmm0
   18004C605  movups xmm1,[rax-70h]
   18004C609  movups [rcx-70h],xmm1
   18004C60D  movups xmm0,[rax-60h]
   18004C611  movups [rcx-60h],xmm0
   18004C615  movups xmm1,[rax-50h]
   18004C619  movups [rcx-50h],xmm1
   18004C61D  movups xmm0,[rax-40h]
   18004C621  movups [rcx-40h],xmm0
   18004C625  movups xmm1,[rax-30h]
   18004C629  movups [rcx-30h],xmm1
   18004C62D  movups xmm0,[rax-20h]
   18004C631  movups [rcx-20h],xmm0
   18004C635  movups xmm1,[rax-10h]
   18004C639  movups [rcx-10h],xmm1
   18004C63D  sub rdx,1
   18004C641  jne short 000000018004C5F0h
   18004C643  mov eax,[rax]
   18004C645  mov [rcx],eax
   18004C647  test byte [rbp+334h],10h
   18004C64E  je near 000000018004C5C6h
   18004C654  cmp [rbp+2C8h],edx
   18004C65A  jl near 000000018004C5C6h
   18004C660  cmp [rbp+294h],dl
   18004C666  je near 000000018004C5C6h
>> 18004C66C  mov rax,[rel 1802C8288h]
   18004C673  movss xmm2,[rdi+4]
   18004C678  movss xmm3,[rdi+8]
   18004C67D  mov [rsp+0C18h],rbx
   18004C685  mov [rsp+0BD8h],r12
   18004C68D  mov [rsp+0BD0h],r13
   18004C695  mov [rsp+0BC8h],r14
   18004C69D  mov [rsp+0BC0h],r15
   18004C6A5  movaps [rsp+0BB0h],xmm6
   18004C6AD  movss xmm6,[rbp+31Ch]
   18004C6B5  movaps [rsp+0BA0h],xmm7
   18004C6BD  movaps [rsp+0B90h],xmm8
   18004C6C6  movss xmm8,[rbp+318h]
   18004C6CF  movaps [rsp+0B80h],xmm9
   18004C6D8  movaps xmm7,xmm8
   18004C6DC  movaps [rsp+0B70h],xmm10
   18004C6E5  movaps xmm9,xmm6
   18004C6E9  movss xmm10,[rdi]
   18004C6EE  movaps [rsp+0B60h],xmm11
   18004C6F7  movaps xmm4,xmm10
   18004C6FB  movss xmm11,[rel 180269D80h]   ; Float32=-0
   18004C704  movaps [rsp+0B50h],xmm12
   18004C70D  movss xmm12,[rbp+308h]
   18004C716  movaps [rsp+0B40h],xmm13
   18004C71F  xorps xmm12,xmm11
   18004C723  movss xmm13,[rbp+30Ch]
   18004C72C  xorps xmm13,xmm11
   18004C730  movaps [rsp+0B30h],xmm14
   18004C739  movss xmm14,[rbp+310h]
   18004C742  xorps xmm14,xmm11
===== 0x1802C8370+38
   18004C6B5  movaps [rsp+0BA0h],xmm7
   18004C6BD  movaps [rsp+0B90h],xmm8
   18004C6C6  movss xmm8,[rbp+318h]
   18004C6CF  movaps [rsp+0B80h],xmm9
   18004C6D8  movaps xmm7,xmm8
   18004C6DC  movaps [rsp+0B70h],xmm10
   18004C6E5  movaps xmm9,xmm6
   18004C6E9  movss xmm10,[rdi]
   18004C6EE  movaps [rsp+0B60h],xmm11
   18004C6F7  movaps xmm4,xmm10
   18004C6FB  movss xmm11,[rel 180269D80h]   ; Float32=-0
   18004C704  movaps [rsp+0B50h],xmm12
   18004C70D  movss xmm12,[rbp+308h]
   18004C716  movaps [rsp+0B40h],xmm13
   18004C71F  xorps xmm12,xmm11
   18004C723  movss xmm13,[rbp+30Ch]
   18004C72C  xorps xmm13,xmm11
   18004C730  movaps [rsp+0B30h],xmm14
   18004C739  movss xmm14,[rbp+310h]
   18004C742  xorps xmm14,xmm11
   18004C746  movss [rbp+308h],xmm12
   18004C74F  movss [rbp+310h],xmm14
   18004C758  movss [rbp+30Ch],xmm13
   18004C761  mov dword [rbp+250h],4
   18004C76B  movss xmm0,[rax+54h]
>> 18004C770  mov rax,[rel 1802C83A8h]
   18004C777  movss [rbp+98h],xmm0
   18004C77F  movss xmm0,[rel 180269804h]   ; Float32=1
   18004C787  movss [rbp+0B20h],xmm10
   18004C790  movss xmm1,[rax+54h]
   18004C795  mov rax,[rel 1802C8048h]
   18004C79C  movss [rbp+9Ch],xmm1
   18004C7A4  movaps xmm1,xmm3
   18004C7A7  mulss xmm9,xmm13
   18004C7AC  subss xmm0,[rax+54h]
   18004C7B1  movaps [rsp+0B20h],xmm15
   18004C7BA  lea rax,[r9+10h]
   18004C7BE  movss xmm15,[rbp+314h]
   18004C7C7  movaps xmm5,xmm15
   18004C7CB  mulss xmm7,xmm12
   18004C7D0  mov [rbp+110h],rax
   18004C7D7  movss [rbp+58h],xmm0
   18004C7DC  movaps xmm0,xmm8
   18004C7E0  mulss xmm0,xmm14
   18004C7E5  mulss xmm5,xmm14
   18004C7EA  subss xmm9,xmm0
   18004C7EF  mulss xmm10,xmm15
   18004C7F4  movaps xmm0,xmm6
   18004C7F7  movss [rbp-50h],xmm12
   18004C7FD  mulss xmm0,xmm12
   18004C802  mulss xmm4,xmm9
   18004C807  subss xmm5,xmm0
   18004C80B  movss [rbp-4Ch],xmm13
   18004C811  movaps xmm0,xmm15
   18004C815  movss [rbp-58h],xmm14
===== 0x1802C9A20+38
   18008F047  movaps xmm12,[rsp+180h]
   18008F050  movaps xmm11,[rsp+190h]
   18008F059  movaps xmm10,[rsp+1A0h]
   18008F062  mov eax,[rsi+28h]
   18008F065  lea rdx,[rbx+1764h]
   18008F06C  movsd xmm0,[rsi+20h]
   18008F071  lea r9,[rbx+0DD0h]
   18008F078  mov rcx,[rbp+130h]
   18008F07F  lea r8,[rbx+10h]
   18008F083  mov [rsp+78h],eax
   18008F087  lea rax,[rbx+1564h]
   18008F08E  mov [rsp+58h],rax
   18008F093  mov rax,[rbp+138h]
   18008F09A  mov [rsp+50h],rax
   18008F09F  lea rax,[rsp+60h]
   18008F0A4  movss [rsp+48h],xmm9
   18008F0AB  movss [rsp+40h],xmm8
   18008F0B2  mov [rsp+38h],rax
   18008F0B7  lea rax,[rsp+70h]
   18008F0BC  mov [rsp+30h],rax
   18008F0C1  mov [rsp+28h],r14d
   18008F0C6  mov [rsp+20h],rdx
   18008F0CB  mov rdx,rbx
   18008F0CE  movsd [rsp+70h],xmm0
   18008F0D4  call 0000000180089D70h
>> 18008F0D9  mov rax,[rel 1802C9A58h]
   18008F0E0  lea r15,[rbx+30h]
   18008F0E4  movaps xmm9,[rsp+1B0h]
   18008F0ED  lea rdi,[rbx+1528h]
   18008F0F4  mov r14d,r12d
   18008F0F7  lea r13,[rbx+13E0h]
   18008F0FE  lea rsi,[rbx+298h]
   18008F105  movss xmm6,[rax+54h]
   18008F10A  mov rax,[rel 1802C9788h]
   18008F111  movss xmm8,[rax+54h]
   18008F117  nop word [rax+rax]
   18008F120  lea rdx,[rbp-70h]
   18008F124  mov rcx,rsi
   18008F127  call 00000001801DF520h
   18008F12C  lea rcx,[r13-150h]
   18008F133  lea rdx,[rbp-30h]
   18008F137  call 00000001801DF520h
   18008F13C  mov r8,rax
   18008F13F  lea rdx,[rbp+10h]
   18008F143  lea rcx,[rbp-70h]
   18008F147  call 00000001801DE040h
   18008F14C  mov rdx,rax
   18008F14F  mov rcx,r13
   18008F152  call 00000001801DE010h
   18008F157  movss xmm2,[r15+4]
   18008F15D  xorps xmm0,xmm0
   18008F160  movss xmm1,[r15]
   18008F165  add rsi,370h
   18008F16C  mov eax,r14d
   18008F16F  subss xmm2,xmm1
===== 0x1802C9750+38
   18008F07F  lea r8,[rbx+10h]
   18008F083  mov [rsp+78h],eax
   18008F087  lea rax,[rbx+1564h]
   18008F08E  mov [rsp+58h],rax
   18008F093  mov rax,[rbp+138h]
   18008F09A  mov [rsp+50h],rax
   18008F09F  lea rax,[rsp+60h]
   18008F0A4  movss [rsp+48h],xmm9
   18008F0AB  movss [rsp+40h],xmm8
   18008F0B2  mov [rsp+38h],rax
   18008F0B7  lea rax,[rsp+70h]
   18008F0BC  mov [rsp+30h],rax
   18008F0C1  mov [rsp+28h],r14d
   18008F0C6  mov [rsp+20h],rdx
   18008F0CB  mov rdx,rbx
   18008F0CE  movsd [rsp+70h],xmm0
   18008F0D4  call 0000000180089D70h
   18008F0D9  mov rax,[rel 1802C9A58h]
   18008F0E0  lea r15,[rbx+30h]
   18008F0E4  movaps xmm9,[rsp+1B0h]
   18008F0ED  lea rdi,[rbx+1528h]
   18008F0F4  mov r14d,r12d
   18008F0F7  lea r13,[rbx+13E0h]
   18008F0FE  lea rsi,[rbx+298h]
   18008F105  movss xmm6,[rax+54h]
>> 18008F10A  mov rax,[rel 1802C9788h]
   18008F111  movss xmm8,[rax+54h]
   18008F117  nop word [rax+rax]
   18008F120  lea rdx,[rbp-70h]
   18008F124  mov rcx,rsi
   18008F127  call 00000001801DF520h
   18008F12C  lea rcx,[r13-150h]
   18008F133  lea rdx,[rbp-30h]
   18008F137  call 00000001801DF520h
   18008F13C  mov r8,rax
   18008F13F  lea rdx,[rbp+10h]
   18008F143  lea rcx,[rbp-70h]
   18008F147  call 00000001801DE040h
   18008F14C  mov rdx,rax
   18008F14F  mov rcx,r13
   18008F152  call 00000001801DE010h
   18008F157  movss xmm2,[r15+4]
   18008F15D  xorps xmm0,xmm0
   18008F160  movss xmm1,[r15]
   18008F165  add rsi,370h
   18008F16C  mov eax,r14d
   18008F16F  subss xmm2,xmm1
   18008F173  mov qword [rdi],0
   18008F17A  inc r14d
   18008F17D  add r13,40h
   18008F181  add r15,370h
   18008F188  cvtsi2ss xmm0,rax
   18008F18D  movss [rdi-4],xmm2
   18008F192  mulss xmm0,xmm8
   18008F197  addss xmm0,xmm14
