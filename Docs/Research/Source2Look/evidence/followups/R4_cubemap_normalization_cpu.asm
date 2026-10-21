===== 0x1802C8660+38
   180050AEC  inc r12d
   180050AEF  add rax,4
   180050AF3  cmp r12d,ecx
   180050AF6  jl short 0000000180050AE0h
   180050AF8  xor r12d,r12d
   180050AFB  xor edx,edx
   180050AFD  lea rcx,[rbp-40h]
   180050B01  mov r8d,14F0h
   180050B07  call 00000001801F9150h
   180050B0C  mov rax,[rel 1802C8568h]
   180050B13  cmp dword [rax+58h],0
   180050B17  mov dword [rbp+0BC0h],0
   180050B21  jne short 0000000180050B2Dh
   180050B23  mov dword [rbp+0BC0h],3F800000h
>> 180050B2D  mov rax,[rel 1802C8698h]
   180050B34  lea r9,[rbp-80h]
   180050B38  mov qword [rbp+0BC4h],0
   180050B43  lea rdx,[rbp-68h]
   180050B47  mov dword [rbp+0BCCh],0
   180050B51  movaps xmm0,xmm7
   180050B54  mov r8d,8310AEF1h
   180050B5A  mov rcx,rdi
   180050B5D  movss xmm2,[rax+54h]
   180050B62  mov rax,[rel 1802C8728h]
   180050B69  movaps xmm1,xmm2
   180050B6C  subss xmm1,xmm7
   180050B70  subss xmm0,[rax+54h]
   180050B75  divss xmm1,xmm0
   180050B79  xorps xmm0,xmm0
   180050B7C  subss xmm2,xmm1
   180050B80  movss [rbp+14A0h],xmm1
   180050B88  movups [rbp-80h],xmm0
   180050B8C  movss [rbp+14A4h],xmm2
   180050B94  call 0000000180036400h
   180050B99  mov rax,[rel 1802C8318h]
   180050BA0  movss xmm9,[rel 180269758h]   ; Float32=0,003921569
180050A44  jmp short 0000000180050A4Dh
180050A46  mov rax,[rdx+288h]
180050A4D  movss xmm7,[rel 180269804h]   ; Float32=1
180050A55  test rax,rax
180050A58  je short 0000000180050A80h
180050A5A  movd xmm0,[rax+88h]
