===== eight
18003C9CA  movaps xmm8,[r11-30h]
18003C9CF  movaps xmm9,[r11-40h]
18003C9D4  movaps xmm10,[r11-50h]
18003C9D9  movaps xmm11,[r11-60h]
18003C9DE  movaps xmm12,[r11-70h]
18003C9E3  movaps xmm13,[r11-80h]
18003C9E8  mov rsp,r11
18003C9EB  pop rbp
18003C9EC  ret
18003C9ED  int3
18003C9EE  int3
18003C9EF  int3
18003C9F0  mov [rsp+10h],rbp
18003C9F5  mov [rsp+18h],rsi
18003C9FA  push rdi
18003C9FB  sub rsp,70h
18003C9FF  mov r9,[rel 1800ED330h]
18003CA06  mov rbp,rcx
18003CA09  lea rcx,[rsp+30h]
18003CA0E  mov qword [rsp+20h],0
18003CA17  xor r8d,r8d
18003CA1A  xor edx,edx
18003CA1C  call 000000018003A9B0h
18003CA21  mov rdi,[rsp+30h]
18003CA26  lea r8,[rsp+40h]
18003CA2B  mov edx,20h
18003CA30  mov rcx,rdi
18003CA33  mov rax,[rdi]
18003CA36  call qword [rax+110h]
18003CA3C  mov rsi,rax
18003CA3F  test rax,rax
18003CA42  je near 000000018003CB36h
18003CA48  movss xmm1,[rbp]
18003CA4D  mov [rsp+80h],rbx
18003CA55  mov rbx,[rsp+40h]
18003CA5A  movaps [rsp+60h],xmm6
18003CA5F  movss xmm6,[rel 1800D3958h]   ; Float32=2
18003CA67  movaps xmm0,xmm6
18003CA6A  movaps [rsp+50h],xmm7
18003CA6F  call qword [rel 1800BD240h]
18003CA75  movss [rbx],xmm0
18003CA79  movaps xmm0,xmm6
18003CA7C  mov ecx,[rbp+4]
18003CA7F  mov [rbx+4],ecx
18003CA82  mov eax,[rbp+8]
18003CA85  mov [rbx+8],eax
18003CA88  mov eax,[rbp+0Ch]
18003CA8B  mov [rbx+0Ch],eax
18003CA8E  mov eax,[rbp+10h]
18003CA91  mov [rbx+10h],eax
18003CA94  mov eax,[rbp+14h]
18003CA97  mov [rbx+14h],eax
18003CA9A  mov eax,[rbp+18h]
18003CA9D  mov [rbx+18h],eax
18003CAA0  movss xmm1,[rbp+1Ch]
18003CAA5  call qword [rel 1800BD240h]
18003CAAB  movss xmm2,[rbp+8]
18003CAB0  mov r8d,20h
18003CAB6  movss xmm7,[rbp+14h]
18003CABB  movaps xmm4,xmm0
18003CABE  mulss xmm4,[rbp+4]
18003CAC3  movaps xmm1,xmm7
18003CAC6  mov rax,[rdi]
18003CAC9  mov rdx,rsi
18003CACC  movss xmm5,[rbp+18h]
18003CAD1  movaps xmm6,xmm2
18003CAD4  mulss xmm6,[rbp+0Ch]
18003CAD9  mov rcx,rdi
18003CADC  mulss xmm1,[rbp+10h]
18003CAE1  addss xmm6,xmm4
18003CAE5  addss xmm4,xmm2
18003CAE9  divss xmm7,xmm5
18003CAED  mulss xmm6,xmm0
18003CAF1  mulss xmm4,xmm0
18003CAF5  movaps xmm0,xmm5
18003CAF8  mulss xmm0,[rbp+10h]
18003CAFD  addss xmm6,xmm1
18003CB01  addss xmm4,xmm0
18003CB05  movss xmm0,[rel 1800D3944h]   ; Float32=1
18003CB0D  divss xmm6,xmm4
