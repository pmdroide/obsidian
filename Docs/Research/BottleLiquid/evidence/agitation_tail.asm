1809DC7FC  movzx eax,byte [rbx+467h]
1809DC803  lea rcx,[rbx+464h]
1809DC80A  movzx r9d,sil
1809DC80E  mov [rsp+20h],al
1809DC812  movzx r8d,dil
1809DC816  movzx edx,r15b
1809DC81A  call 00000001801667C0h
1809DC81F  cmp byte [rbx+0CD0h],0
1809DC826  movaps xmm14,[rsp+90h]
1809DC82F  movaps xmm12,[rsp+0B0h]
1809DC838  movaps xmm7,[rsp+0E0h]
1809DC840  jne short 00000001809DC8AAh
1809DC842  test sil,sil
1809DC845  jne short 00000001809DC84Ch
1809DC847  test dil,dil
1809DC84A  je short 00000001809DC8AAh
1809DC84C  xor r9d,r9d
1809DC84F  mov qword [rsp+20h],0
1809DC858  movaps xmm2,xmm6
1809DC85B  lea rdx,[rel 1809DC010h]
1809DC862  mov rcx,rbx
1809DC865  call 00000001801A49D0h
1809DC86A  mov rdi,[rel 18125D8F0h]
1809DC871  test rdi,rdi
1809DC874  je short 00000001809DC897h
1809DC876  cmp byte [rdi+45h],0
1809DC87A  jne short 00000001809DC892h
1809DC87C  cmp byte [rdi+44h],0
1809DC880  jne short 00000001809DC892h
1809DC882  mov rax,[rdi+28h]
1809DC886  test rax,rax
1809DC889  je short 00000001809DC892h
1809DC88B  mov ecx,1
1809DC890  call rax
1809DC892  movss xmm6,[rdi+34h]
1809DC897  addss xmm6,xmm9
1809DC89C  xor r8d,r8d
1809DC89F  mov rcx,rbx
1809DC8A2  movaps xmm1,xmm6
1809DC8A5  call 00000001801A4B60h
1809DC8AA  movaps xmm6,[rsp+0F0h]
1809DC8B2  mov rsi,[rsp+128h]
1809DC8BA  movaps xmm9,[rsp+0C0h]
1809DC8C3  mov r14,[rsp+138h]
1809DC8CB  mov r15,[rsp+100h]
1809DC8D3  mov rdi,[rsp+130h]
1809DC8DB  add rsp,108h
1809DC8E2  pop rbx
1809DC8E3  pop rbp
1809DC8E4  ret
1809DC8E5  movsd xmm0,[rel 1814318A0h]
1809DC8ED  lea rdx,[rsp+70h]
1809DC8F2  mov eax,[rel 1814318A8h]
1809DC8F8  mov rcx,rbx
1809DC8FB  movsd [rsp+70h],xmm0
1809DC901  mov [rsp+78h],eax
1809DC905  call 00000001809D94B0h
1809DC90A  jmp short 00000001809DC8C3h
1809DC90C  int3
1809DC90D  int3
1809DC90E  int3
1809DC90F  int3
1809DC910  mov rax,rsp
1809DC913  sub rsp,88h
1809DC91A  mov [rax+18h],rbp
1809DC91E  xor ebp,ebp
1809DC920  mov [rax-8],rsi
1809DC924  mov rsi,rcx
1809DC927  mov [rax-48h],ebp
1809DC92A  mov [rax-40h],rbp
1809DC92E  mov [rax-38h],rbp
1809DC932  cmp [rcx+0C91h],bpl
1809DC939  je short 00000001809DC969h
1809DC93B  cmp [rcx+0CD0h],bpl
1809DC942  jne short 00000001809DC969h
1809DC944  movzx eax,byte [rsi+467h]
1809DC94B  add rcx,464h
1809DC952  xor r9d,r9d
1809DC955  mov [rsp+20h],al
1809DC959  xor r8d,r8d
1809DC95C  movzx edx,byte [rcx]
1809DC95F  call 00000001801667C0h
