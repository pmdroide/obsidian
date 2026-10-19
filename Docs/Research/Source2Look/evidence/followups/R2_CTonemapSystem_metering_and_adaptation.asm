18010C200  mov [rsp+18h],rsi
18010C205  push rdi
18010C206  sub rsp,20h
18010C20A  xor edi,edi
18010C20C  mov rsi,rcx
18010C20F  cmp [rcx+33Ch],edi
18010C215  jle short 000000018010C283h
18010C217  mov [rsp+38h],rbx
18010C21C  lea rbx,[rcx+10h]
18010C220  mov rdx,[rbx]
18010C223  test rdx,rdx
18010C226  je short 000000018010C270h
18010C228  mov eax,[rbx-8]
18010C22B  dec eax
18010C22D  cmp eax,1
18010C230  ja short 000000018010C270h
18010C232  mov eax,[rbx+8]
18010C235  add eax,2
18010C238  cmp [rsi+288h],eax
18010C23E  jle short 000000018010C270h
18010C240  mov rcx,[rel 1802BB2D0h]
18010C247  lea r8,[rsp+30h]
18010C24C  mov rax,[rcx]
18010C24F  call qword [rax+198h]
18010C255  test eax,eax
18010C257  jne short 000000018010C270h
18010C259  cmp [rbx+18h],al
18010C25C  jne short 000000018010C265h
18010C25E  mov eax,[rsp+30h]
18010C262  mov [rbx+0Ch],eax
18010C265  mov byte [rbx+18h],0
18010C269  mov dword [rbx-8],3
18010C270  inc edi
18010C272  add rbx,28h
18010C276  cmp edi,[rsi+33Ch]
18010C27C  jl short 000000018010C220h
18010C27E  mov rbx,[rsp+38h]
18010C283  mov rsi,[rsp+40h]
18010C288  add rsp,20h
18010C28C  pop rdi
18010C28D  ret
18010C290  mov [rsp+10h],rbx
18010C295  mov [rsp+18h],rbp
18010C29A  mov [rsp+20h],rsi
18010C29F  push rdi
18010C2A0  sub rsp,40h
18010C2A4  movsxd rdi,[rcx+33Ch]
18010C2AB  xor ebp,ebp
18010C2AD  mov rsi,rdx
18010C2B0  mov rbx,rcx
18010C2B3  mov r8d,ebp
18010C2B6  mov r9d,ebp
18010C2B9  test edi,edi
18010C2BB  jle short 000000018010C2E0h
18010C2BD  lea rax,[rcx+8]
18010C2C1  cmp [rax],ebp
18010C2C3  je short 000000018010C2D6h
18010C2C5  inc r8d
18010C2C8  inc r9
18010C2CB  add rax,28h
18010C2CF  cmp r9,rdi
18010C2D2  jl short 000000018010C2C1h
18010C2D4  jmp short 000000018010C2E0h
18010C2D6  cmp r8d,0FFFFFFFFh
18010C2DA  jne near 000000018010C396h
18010C2E0  or r8,0FFFFFFFFFFFFFFFFh
18010C2E4  mov edx,ebp
18010C2E6  test edi,edi
18010C2E8  jle short 000000018010C338h
18010C2EA  mov [rsp+50h],r14
18010C2EF  lea r11,[r8-27h]
18010C2F3  mov r14,0FFFFFFFFFFFFFFE8h
18010C2FA  add rcx,18h
18010C2FE  sub r14,rbx
18010C301  mov r9,rbp
18010C304  mov r10,r8
18010C307  cmp dword [rcx-10h],3
18010C30B  jne short 000000018010C326h
18010C30D  cmp r10,0FFFFFFFFFFFFFFFFh
18010C311  je short 000000018010C31Ch
18010C313  mov eax,[r11+rbx+18h]
18010C318  cmp [rcx],eax
18010C31A  jge short 000000018010C326h
18010C31C  mov r8d,edx
18010C31F  lea r11,[r14+rcx]
18010C323  mov r10,r9
18010C326  inc edx
18010C328  inc r9
18010C32B  add rcx,28h
18010C32F  cmp edx,edi
18010C331  jl short 000000018010C307h
18010C333  mov r14,[rsp+50h]
18010C338  cmp r8d,0FFFFFFFFh
18010C33C  jne short 000000018010C396h
18010C33E  mov eax,[rel 1802D40F8h]
18010C344  cmp eax,5
18010C347  jge short 000000018010C37Dh
18010C349  mov ecx,[rel 1802CB7F8h]
18010C34F  lea edx,[r8+3]
18010C353  inc eax
18010C355  mov [rel 1802D40F8h],eax
18010C35B  call qword [rel 180224650h]
18010C361  test al,al
18010C363  je short 000000018010C37Dh
18010C365  mov ecx,[rel 1802CB7F8h]
18010C36B  lea r8,[rel 180268028h]
18010C372  mov edx,2
18010C377  call qword [rel 180224560h]
18010C37D  mov [rsp+28h],rbp
18010C382  mov [rsp+20h],rbp
18010C387  mov [rsp+30h],bp
18010C38C  mov [rsp+38h],rbp
18010C391  jmp 000000018010C462h
18010C396  movsxd rax,r8d
18010C399  mov dword [rsp+28h],3F666666h
18010C3A1  mov dword [rsp+2Ch],3F666666h
18010C3A9  lea rdi,[rax+rax*4]
18010C3AD  cmp [rbx+rdi*8+8],ebp
18010C3B1  movss xmm0,[rbx+rdi*8+20h]
18010C3B7  movss xmm1,[rbx+rdi*8+24h]
18010C3BD  setne bpl
18010C3C1  inc ebp
18010C3C3  movss [rsp+20h],xmm0
18010C3C9  mov [rbx+rdi*8+8],ebp
18010C3CD  mov eax,[rbx+288h]
18010C3D3  mov [rbx+rdi*8+18h],eax
18010C3D7  movzx eax,byte [rbx+338h]
18010C3DE  mov [rsp+30h],al
18010C3E2  mov rax,[rel 1802D30A8h]
18010C3E9  movss [rsp+24h],xmm1
18010C3EF  mov ecx,[rax+58h]
18010C3F2  test ecx,ecx
18010C3F4  js short 000000018010C3FBh
18010C3F6  setne [rsp+30h]
18010C3FB  cmp qword [rbx+rdi*8+10h],0
18010C401  movzx eax,byte [rbx+339h]
18010C408  mov [rsp+31h],al
18010C40C  jne short 000000018010C458h
18010C40E  mov rcx,[rel 1802BB2D0h]
18010C415  mov edx,1
18010C41A  mov rax,[rcx]
18010C41D  call qword [rax+190h]
18010C423  mov [rbx+rdi*8+10h],rax
18010C428  test rax,rax
18010C42B  jne short 000000018010C458h
18010C42D  mov ecx,[rel 1802CB7F8h]
18010C433  lea edx,[rax+2]
18010C436  call qword [rel 180224650h]
18010C43C  test al,al
18010C43E  je short 000000018010C458h
18010C440  mov ecx,[rel 1802CB7F8h]
18010C446  lea r8,[rel 180267FD0h]
18010C44D  mov edx,2
18010C452  call qword [rel 180224560h]
18010C458  mov rax,[rbx+rdi*8+10h]
18010C45D  mov [rsp+38h],rax
18010C462  movups xmm0,[rsp+20h]
18010C467  mov rbx,[rsp+58h]
18010C46C  mov rax,rsi
18010C46F  movups xmm1,[rsp+30h]
18010C474  mov rbp,[rsp+60h]
18010C479  movups [rsi],xmm0
18010C47C  movups [rsi+10h],xmm1
18010C480  mov rsi,[rsp+68h]
18010C485  add rsp,40h
18010C489  pop rdi
18010C48A  ret
18010C490  sub rsp,68h
18010C494  movaps xmm0,[rel 180269C20h]   ; Packed128_Float32=0
18010C49B  xor eax,eax
18010C49D  movaps xmm1,[rel 180269C30h]   ; Packed128_Float32=0,5825
18010C4A4  xor r8d,r8d
18010C4A7  mov r9,rcx
18010C4AA  movss xmm2,[rel 180269804h]   ; Float32=1
18010C4B2  movaps [rsp],xmm0
18010C4B6  movaps [rsp+10h],xmm1
18010C4BB  mov dword [rsp+20h],3F800000h
18010C4C3  mov [rsp+24h],rax
18010C4C8  mov [rsp+2Ch],rax
18010C4CD  mov [rsp+34h],rax
18010C4D2  mov [rsp+3Ch],rax
18010C4D7  cmp [rcx+339h],al
18010C4DD  je near 000000018010C619h
18010C4E3  movsxd r10,[rcx+33Ch]
18010C4EA  mov ecx,r8d
18010C4ED  lea eax,[r10+1]
18010C4F1  test r10d,r10d
18010C4F4  js near 000000018010C602h
18010C4FA  cmp eax,8
18010C4FD  jb near 000000018010C5ADh
18010C503  movd xmm0,[r9+33Ch]
18010C50C  movdqa xmm5,[rel 180269BA0h]
18010C514  pshufd xmm0,xmm0,0
18010C519  movaps [rsp+50h],xmm6
18010C51E  and eax,80000007h
18010C523  jge short 000000018010C52Ch
18010C525  dec eax
18010C527  or eax,0FFFFFFF8h
18010C52A  inc eax
18010C52C  movaps xmm6,[rel 180269DE0h]   ; Packed128_Float32=-8
18010C533  lea rdx,[rsp+10h]
18010C538  movaps xmm4,[rel 180269CE0h]   ; Packed128_Float32=13
18010C53F  mov r11d,r10d
18010C542  cvtdq2ps xmm0,xmm0
18010C545  sub r11d,eax
18010C548  rcpps xmm3,xmm0
18010C54B  movaps xmm1,xmm3
18010C54E  mulps xmm1,xmm3
18010C551  addps xmm3,xmm3
18010C554  mulps xmm1,xmm0
18010C557  subps xmm3,xmm1
18010C55A  nop word [rax+rax]
18010C560  movd xmm0,ecx
18010C564  lea eax,[rcx+4]
18010C567  pshufd xmm0,xmm0,0
18010C56C  add ecx,8
18010C56F  paddd xmm0,xmm5
18010C573  cvtdq2ps xmm1,xmm0
18010C576  movd xmm0,eax
18010C57A  mulps xmm1,xmm3
18010C57D  pshufd xmm0,xmm0,0
18010C582  paddd xmm0,xmm5
18010C586  mulps xmm1,xmm4
18010C589  addps xmm1,xmm6
18010C58C  movups [rdx-10h],xmm1
18010C590  cvtdq2ps xmm1,xmm0
18010C593  mulps xmm1,xmm3
18010C596  mulps xmm1,xmm4
18010C599  addps xmm1,xmm6
18010C59C  movups [rdx],xmm1
18010C59F  add rdx,20h
18010C5A3  cmp ecx,r11d
18010C5A6  jle short 000000018010C560h
18010C5A8  movaps xmm6,[rsp+50h]
18010C5AD  cmp ecx,r10d
18010C5B0  jg short 000000018010C602h
18010C5B2  movd xmm0,[r9+33Ch]
18010C5BB  lea rdx,[rsp]
18010C5BF  movss xmm1,[rel 180269884h]   ; Float32=13
18010C5C7  movss xmm3,[rel 180269878h]   ; Float32=8
18010C5CF  cvtdq2ps xmm0,xmm0
18010C5D2  movsxd rax,ecx
18010C5D5  lea rdx,[rdx+rax*4]
18010C5D9  divss xmm2,xmm0
18010C5DD  nop dword [rax]
18010C5E0  movd xmm0,ecx
18010C5E4  inc ecx
18010C5E6  cvtdq2ps xmm0,xmm0
18010C5E9  mulss xmm0,xmm2
18010C5ED  mulss xmm0,xmm1
18010C5F1  subss xmm0,xmm3
18010C5F5  movss [rdx],xmm0
18010C5F9  add rdx,4
18010C5FD  cmp ecx,r10d
18010C600  jle short 000000018010C5E0h
18010C602  movss xmm0,[rel 1802699A4h]   ; Float32=-3,4028235E+38
18010C60A  movss [rsp],xmm0
18010C60F  mov dword [rsp+r10*4],7F7FFFFFh
18010C617  jmp short 000000018010C68Ah
18010C619  movss xmm4,[rcx+324h]
18010C621  mulss xmm4,[rel 180269764h]   ; Float32=0,01
18010C629  movss xmm0,[rcx+32Ch]
18010C631  mulss xmm0,[rel 180269764h]   ; Float32=0,01
18010C639  movaps xmm1,xmm4
18010C63C  subss xmm1,[rel 180269770h]   ; Float32=0,0175
18010C644  movss [rsp+4],xmm0
18010C64A  addss xmm0,xmm0
18010C64E  movss [rsp+10h],xmm1
18010C654  movss [rsp+8],xmm0
18010C65A  movaps xmm0,xmm4
18010C65D  subss xmm0,[rel 180269780h]   ; Float32=0,0675
18010C665  movss [rsp+0Ch],xmm0
18010C66B  movaps xmm0,xmm4
18010C66E  addss xmm0,[rel 180269770h]   ; Float32=0,0175
18010C676  addss xmm4,[rel 180269780h]   ; Float32=0,0675
18010C67E  movss [rsp+14h],xmm0
18010C684  movss [rsp+18h],xmm4
18010C68A  cmp [r9+33Ch],r8d
18010C691  jle short 000000018010C6D4h
18010C693  mov rcx,r8
18010C696  lea rdx,[r9+24h]
18010C69A  nop word [rax+rax]
18010C6A0  test rcx,rcx
18010C6A3  jle short 000000018010C6B5h
18010C6A5  movss xmm0,[rsp+rcx*4-4]
18010C6AB  maxss xmm0,[rsp+rcx*4]
18010C6B0  movss [rsp+rcx*4],xmm0
18010C6B5  mov eax,[rsp+rcx*4]
18010C6B8  inc r8d
18010C6BB  mov [rdx-4],eax
18010C6BE  mov eax,[rsp+rcx*4+4]
18010C6C2  inc rcx
18010C6C5  mov [rdx],eax
18010C6C7  add rdx,28h
18010C6CB  cmp r8d,[r9+33Ch]
18010C6D2  jl short 000000018010C6A0h
18010C6D4  add rsp,68h
18010C6D8  ret
18010C6E0  sub rsp,48h
18010C6E4  movsxd r9,[rcx+33Ch]
18010C6EB  xor r8d,r8d
18010C6EE  movaps [rsp+30h],xmm6
18010C6F3  movaps xmm6,xmm2
18010C6F6  movaps [rsp],xmm9
18010C6FB  movaps xmm9,xmm1
18010C6FF  mov r10,rcx
18010C702  mov rdx,r9
18010C705  test r9d,r9d
18010C708  jle near 000000018010C857h
18010C70E  lea rax,[rcx+8]
18010C712  cmp dword [rax],1
18010C715  jbe short 000000018010C721h
18010C717  mov ecx,[rax+14h]
18010C71A  test ecx,ecx
18010C71C  js short 000000018010C721h
18010C71E  add r8d,ecx
18010C721  add rax,28h
18010C725  sub rdx,1
18010C729  jne short 000000018010C712h
18010C72B  test r8d,r8d
18010C72E  je near 000000018010C857h
18010C734  movaps [rsp+20h],xmm7
18010C739  sub r9d,1
18010C73D  xorps xmm7,xmm7
18010C740  movaps [rsp+10h],xmm8
18010C746  movaps xmm5,xmm7
18010C749  movsxd rdx,r9d
18010C74C  movaps xmm4,xmm7
18010C74F  js short 000000018010C7C0h
18010C751  movss xmm8,[rel 180269764h]   ; Float32=0,01
18010C75A  lea rcx,[rdx*4+1]
18010C762  add rcx,rdx
18010C765  lea rcx,[r10+rcx*8]
18010C769  nop dword [rax]
18010C770  cmp dword [rcx],1
18010C773  jbe short 000000018010C7C0h
18010C775  mov eax,[rcx+14h]
18010C778  test eax,eax
18010C77A  js short 000000018010C7C0h
18010C77C  movss xmm3,[rcx+1Ch]
18010C781  movaps xmm1,xmm9
18010C785  subss xmm3,[rcx+18h]
18010C78A  movd xmm2,eax
18010C78E  movd xmm0,r8d
18010C793  cvtdq2ps xmm2,xmm2
18010C796  cvtdq2ps xmm0,xmm0
18010C799  mulss xmm1,xmm8
18010C79E  divss xmm2,xmm0
18010C7A2  subss xmm1,xmm4
18010C7A6  comiss xmm2,xmm1
18010C7A9  jae short 000000018010C7E2h
18010C7AB  dec r9d
18010C7AE  addss xmm4,xmm2
18010C7B2  sub rcx,28h
18010C7B6  addss xmm5,xmm3
18010C7BA  sub rdx,1
18010C7BE  jns short 000000018010C770h
18010C7C0  movss xmm0,[rel 180269970h]   ; Float32=-1
18010C7C8  movaps xmm7,[rsp+20h]
18010C7CD  movaps xmm8,[rsp+10h]
18010C7D3  movaps xmm6,[rsp+30h]
18010C7D8  movaps xmm9,[rsp]
18010C7DD  add rsp,48h
18010C7E1  ret
18010C7E2  comiss xmm6,xmm7
18010C7E5  jb short 000000018010C808h
18010C7E7  movsxd rax,r9d
18010C7EA  mulss xmm6,xmm8
18010C7EF  lea rcx,[rax+rax*4]
18010C7F3  comiss xmm6,[r10+rcx*8+20h]
18010C7F9  jb short 000000018010C808h
18010C7FB  comiss xmm6,[r10+rcx*8+24h]
18010C801  ja short 000000018010C808h
18010C803  movaps xmm0,xmm6
18010C806  jmp short 000000018010C7C8h
18010C808  divss xmm1,xmm2
18010C80C  movsxd rcx,r9d
18010C80F  lea rax,[rcx+rcx*4]
18010C813  movss xmm2,[rel 180269804h]   ; Float32=1
18010C81B  movss xmm0,[r10+rax*8+24h]
18010C822  mulss xmm1,xmm3
18010C826  addss xmm1,xmm5
18010C82A  subss xmm2,xmm1
18010C82E  minss xmm0,xmm2
18010C832  comiss xmm0,[r10+rax*8+20h]
18010C838  lea rax,[rcx+rcx*4]
18010C83C  jae short 000000018010C847h
18010C83E  movss xmm0,[r10+rax*8+20h]
18010C845  jmp short 000000018010C7C8h
18010C847  movss xmm0,[r10+rax*8+24h]
18010C84E  minss xmm0,xmm2
18010C852  jmp 000000018010C7C8h
18010C857  movss xmm0,[rel 180269970h]   ; Float32=-1
18010C85F  movaps xmm6,[rsp+30h]
18010C864  movaps xmm9,[rsp]
18010C869  add rsp,48h
18010C86D  ret
18010C870  sub rsp,48h
18010C874  cmp byte [rcx+339h],0
18010C87B  mov r11,rcx
18010C87E  je short 000000018010C889h
18010C880  add rsp,48h
18010C884  jmp 000000018010C950h
18010C889  movss xmm1,[rcx+328h]
18010C891  movaps [rsp+30h],xmm6
18010C896  movss xmm6,[rcx+324h]
18010C89E  movaps xmm2,xmm6
18010C8A1  movaps [rsp+20h],xmm8
18010C8A7  call 000000018010C6E0h
18010C8AC  xorps xmm8,xmm8
18010C8B0  comiss xmm0,xmm8
18010C8B4  jae short 000000018010C8C1h
18010C8B6  movaps xmm0,xmm6
18010C8B9  mulss xmm0,[rel 180269764h]   ; Float32=0,01
18010C8C1  maxss xmm0,[rel 180269740h]   ; Float32=0,001
18010C8C9  mulss xmm6,[rel 180269764h]   ; Float32=0,01
18010C8D1  mov rcx,r11
18010C8D4  movss xmm2,[rel 180269970h]   ; Float32=-1
18010C8DC  movss xmm1,[rel 1802698ACh]   ; Float32=50
18010C8E4  divss xmm6,xmm0
18010C8E8  call 000000018010C6E0h
18010C8ED  comiss xmm0,xmm8
18010C8F1  movaps xmm8,[rsp+20h]
18010C8F7  jbe short 000000018010C915h
18010C8F9  movss xmm1,[r11+32Ch]
18010C902  mulss xmm1,[rel 180269764h]   ; Float32=0,01
18010C90A  divss xmm1,xmm0
18010C90E  maxss xmm1,xmm6
18010C912  movaps xmm6,xmm1
18010C915  mulss xmm6,[r11+290h]
18010C91E  movss xmm0,[r11+318h]
18010C927  comiss xmm6,xmm0
18010C92A  jb short 000000018010C939h
18010C92C  movss xmm0,[r11+31Ch]
18010C935  minss xmm0,xmm6
18010C939  maxss xmm0,[rel 180269740h]   ; Float32=0,001
18010C941  movaps xmm6,[rsp+30h]
18010C946  add rsp,48h
18010C94A  ret
18010C950  mov rax,rsp
18010C953  sub rsp,0D8h
18010C95A  mov r11d,[rcx+33Ch]
18010C961  xor r9d,r9d
18010C964  movss xmm3,[rel 180269884h]   ; Float32=13
18010C96C  mov edx,r9d
18010C96F  movss xmm4,[rel 180269878h]   ; Float32=8
18010C977  movaps [rax-18h],xmm6
18010C97B  movss xmm6,[rel 1802697C0h]   ; Float32=0,5
18010C983  lea r10d,[r9+2]
18010C987  mov [rax-8],rdi
18010C98B  mov rdi,rcx
18010C98E  movaps [rax-28h],xmm7
18010C992  movss xmm7,[rel 180269804h]   ; Float32=1
18010C99A  movaps [rax-38h],xmm8
18010C99F  xorps xmm8,xmm8
18010C9A3  movaps xmm2,xmm8
18010C9A7  cmp r11d,4
18010C9AB  jl near 000000018010CACEh
18010C9B1  mov [rax+8],rbx
18010C9B5  lea r8,[rcx+44h]
18010C9B9  movd xmm0,r11d
18010C9BE  movaps xmm5,xmm7
18010C9C1  cvtdq2ps xmm0,xmm0
18010C9C4  mov [rax+10h],rsi
18010C9C8  mov ebx,r10d
18010C9CB  lea esi,[r11-3]
18010C9CF  mov ecx,r9d
18010C9D2  divss xmm5,xmm0
18010C9D6  nop word [rax+rax]
18010C9E0  movd xmm0,edx
18010C9E4  lea eax,[rbx-1]
18010C9E7  cvtdq2ps xmm0,xmm0
18010C9EA  add edx,4
18010C9ED  movd xmm1,ebx
18010C9F1  addss xmm0,xmm6
18010C9F5  cvtdq2ps xmm1,xmm1
18010C9F8  mulss xmm0,xmm5
18010C9FC  addss xmm1,xmm6
18010CA00  mulss xmm0,xmm3
18010CA04  mulss xmm1,xmm5
18010CA08  subss xmm0,xmm4
18010CA0C  mulss xmm1,xmm3
18010CA10  movss [rsp+rcx+60h],xmm0
18010CA16  movd xmm0,[r8-28h]
18010CA1C  subss xmm1,xmm4
18010CA20  cvtdq2ps xmm0,xmm0
18010CA23  movss [rsp+rcx+68h],xmm1
18010CA29  addss xmm2,xmm0
18010CA2D  movd xmm0,eax
18010CA31  cvtdq2ps xmm0,xmm0
18010CA34  lea eax,[rbx+1]
18010CA37  add ebx,4
18010CA3A  movss [rsp+rcx+20h],xmm2
18010CA40  addss xmm0,xmm6
18010CA44  mulss xmm0,xmm5
18010CA48  mulss xmm0,xmm3
18010CA4C  subss xmm0,xmm4
18010CA50  movss [rsp+rcx+64h],xmm0
18010CA56  movd xmm0,[r8]
18010CA5B  cvtdq2ps xmm0,xmm0
18010CA5E  addss xmm2,xmm0
18010CA62  movd xmm0,[r8+28h]
18010CA68  cvtdq2ps xmm0,xmm0
18010CA6B  movss [rsp+rcx+24h],xmm2
18010CA71  addss xmm2,xmm0
18010CA75  movd xmm0,eax
18010CA79  cvtdq2ps xmm0,xmm0
18010CA7C  movss [rsp+rcx+28h],xmm2
18010CA82  addss xmm0,xmm6
18010CA86  mulss xmm0,xmm5
18010CA8A  mulss xmm0,xmm3
18010CA8E  subss xmm0,xmm4
18010CA92  movss [rsp+rcx+6Ch],xmm0
18010CA98  movd xmm0,[r8+50h]
18010CA9E  add r8,0A0h
18010CAA5  cvtdq2ps xmm0,xmm0
18010CAA8  addss xmm2,xmm0
18010CAAC  movss [rsp+rcx+2Ch],xmm2
18010CAB2  add rcx,10h
18010CAB6  cmp edx,esi
18010CAB8  jl near 000000018010C9E0h
18010CABE  mov rsi,[rsp+0E8h]
18010CAC6  mov rbx,[rsp+0E0h]
18010CACE  cmp edx,r11d
18010CAD1  jge short 000000018010CB37h
18010CAD3  movsxd rcx,edx
18010CAD6  lea r8,[rdi+1Ch]
18010CADA  movd xmm0,r11d
18010CADF  movaps xmm1,xmm7
18010CAE2  cvtdq2ps xmm0,xmm0
18010CAE5  lea rax,[rcx+rcx*4]
18010CAE9  lea r8,[r8+rax*8]
18010CAED  lea rcx,[rcx*4]
18010CAF5  divss xmm1,xmm0
18010CAF9  movd xmm0,edx
18010CAFD  inc edx
18010CAFF  cvtdq2ps xmm0,xmm0
18010CB02  addss xmm0,xmm6
18010CB06  mulss xmm0,xmm1
18010CB0A  mulss xmm0,xmm3
18010CB0E  subss xmm0,xmm4
18010CB12  movss [rsp+rcx+60h],xmm0
18010CB18  movd xmm0,[r8]
18010CB1D  add r8,28h
18010CB21  cvtdq2ps xmm0,xmm0
18010CB24  addss xmm2,xmm0
18010CB28  movss [rsp+rcx+20h],xmm2
18010CB2E  add rcx,4
18010CB32  cmp edx,r11d
18010CB35  jl short 000000018010CAF9h
18010CB37  mov rax,[rel 1802D2D48h]
18010CB3E  mov rdi,[rsp+0D0h]
18010CB46  movss xmm4,[rax+54h]
18010CB4B  comiss xmm4,xmm8
18010CB4F  jae short 000000018010CB54h
18010CB51  movaps xmm4,xmm6
18010CB54  mov rax,[rel 1802D2DD8h]
18010CB5B  minss xmm4,xmm7
18010CB5F  movss xmm3,[rax+54h]
18010CB64  comiss xmm3,xmm8
18010CB68  movaps xmm8,[rsp+0A0h]
18010CB71  jae short 000000018010CB76h
18010CB73  movaps xmm3,xmm6
18010CB76  minss xmm3,xmm7
18010CB7A  mulss xmm4,xmm2
18010CB7E  or r8d,0FFFFFFFFh
18010CB82  movaps xmm7,[rsp+0B0h]
18010CB8A  mov edx,r8d
18010CB8D  mulss xmm3,xmm2
18010CB91  cmp r11d,4
18010CB95  jl near 000000018010CC38h
18010CB9B  lea ecx,[r11-3]
18010CB9F  lea rax,[rsp+24h]
18010CBA4  movss xmm0,[rax-4]
18010CBA9  comiss xmm0,xmm4
18010CBAC  jb short 000000018010CBB6h
18010CBAE  cmp r8d,0FFFFFFFFh
18010CBB2  cmove r8d,r9d
18010CBB6  comiss xmm0,xmm3
18010CBB9  jb short 000000018010CBC2h
18010CBBB  cmp edx,0FFFFFFFFh
18010CBBE  cmove edx,r9d
18010CBC2  movss xmm0,[rax]
18010CBC6  comiss xmm0,xmm4
18010CBC9  jb short 000000018010CBD5h
18010CBCB  cmp r8d,0FFFFFFFFh
18010CBCF  jne short 000000018010CBD5h
18010CBD1  lea r8d,[r10-1]
18010CBD5  comiss xmm0,xmm3
18010CBD8  jb short 000000018010CBE3h
18010CBDA  cmp edx,0FFFFFFFFh
18010CBDD  jne short 000000018010CBE3h
18010CBDF  lea edx,[r10-1]
18010CBE3  movss xmm0,[rax+4]
18010CBE8  comiss xmm0,xmm4
18010CBEB  jb short 000000018010CBF5h
18010CBED  cmp r8d,0FFFFFFFFh
18010CBF1  cmove r8d,r10d
18010CBF5  comiss xmm0,xmm3
18010CBF8  jb short 000000018010CC01h
18010CBFA  cmp edx,0FFFFFFFFh
18010CBFD  cmove edx,r10d
18010CC01  movss xmm0,[rax+8]
18010CC06  comiss xmm0,xmm4
18010CC09  jb short 000000018010CC15h
18010CC0B  cmp r8d,0FFFFFFFFh
18010CC0F  jne short 000000018010CC15h
18010CC11  lea r8d,[r10+1]
18010CC15  comiss xmm0,xmm3
18010CC18  jb short 000000018010CC23h
18010CC1A  cmp edx,0FFFFFFFFh
18010CC1D  jne short 000000018010CC23h
18010CC1F  lea edx,[r10+1]
18010CC23  add rax,10h
18010CC27  add r9d,4
18010CC2B  add r10d,4
18010CC2F  cmp r9d,ecx
18010CC32  jl near 000000018010CBA4h
18010CC38  cmp r9d,r11d
18010CC3B  jge short 000000018010CC72h
18010CC3D  movsxd rax,r9d
18010CC40  lea rcx,[rsp+20h]
18010CC45  lea rcx,[rcx+rax*4]
18010CC49  movss xmm0,[rcx]
18010CC4D  comiss xmm0,xmm4
18010CC50  jb short 000000018010CC5Ah
18010CC52  cmp r8d,0FFFFFFFFh
18010CC56  cmove r8d,r9d
18010CC5A  comiss xmm0,xmm3
18010CC5D  jb short 000000018010CC66h
18010CC5F  cmp edx,0FFFFFFFFh
18010CC62  cmove edx,r9d
18010CC66  add rcx,4
18010CC6A  inc r9d
18010CC6D  cmp r9d,r11d
18010CC70  jl short 000000018010CC49h
18010CC72  test r8d,r8d
18010CC75  jle short 000000018010CCB3h
18010CC77  lea eax,[r8-1]
18010CC7B  movsxd rcx,eax
18010CC7E  movsxd rax,r8d
18010CC81  movss xmm1,[rsp+rcx*4+20h]
18010CC87  movss xmm0,[rsp+rax*4+20h]
18010CC8D  subss xmm4,xmm1
18010CC91  movss xmm2,[rsp+rcx*4+60h]
18010CC97  subss xmm0,xmm1
18010CC9B  movss xmm1,[rsp+rax*4+60h]
18010CCA1  subss xmm1,xmm2
18010CCA5  divss xmm4,xmm0
18010CCA9  mulss xmm4,xmm1
18010CCAD  addss xmm4,xmm2
18010CCB1  jmp short 000000018010CCBCh
18010CCB3  movsxd rax,r8d
18010CCB6  movss xmm4,[rsp+rax*4+60h]
18010CCBC  test edx,edx
18010CCBE  jle short 000000018010CCFBh
18010CCC0  lea eax,[rdx-1]
18010CCC3  movsxd rcx,eax
18010CCC6  movsxd rax,edx
18010CCC9  movss xmm1,[rsp+rcx*4+20h]
18010CCCF  movss xmm0,[rsp+rax*4+20h]
18010CCD5  subss xmm3,xmm1
18010CCD9  movss xmm2,[rsp+rcx*4+60h]
18010CCDF  subss xmm0,xmm1
18010CCE3  movss xmm1,[rsp+rax*4+60h]
18010CCE9  subss xmm1,xmm2
18010CCED  divss xmm3,xmm0
18010CCF1  mulss xmm3,xmm1
18010CCF5  addss xmm3,xmm2
18010CCF9  jmp short 000000018010CD04h
18010CCFB  movsxd rax,edx
18010CCFE  movss xmm3,[rsp+rax*4+60h]
18010CD04  movss xmm0,[rel 180269830h]   ; Float32=2
18010CD0C  addss xmm3,xmm4
18010CD10  mulss xmm3,xmm6
18010CD14  movaps xmm1,xmm3
18010CD17  call 0000000180207E30h
18010CD1C  movss xmm1,[rel 180269798h]   ; Float32=0,18
18010CD24  movaps xmm6,[rsp+0C0h]
18010CD2C  divss xmm1,xmm0
18010CD30  movaps xmm0,xmm1
18010CD33  add rsp,0D8h
18010CD3A  ret
18010CD40  xorps xmm0,xmm0
18010CD43  mov rdx,rcx
18010CD46  comiss xmm1,xmm0
18010CD49  ja short 000000018010CD63h
18010CD4B  movss xmm1,[rcx+31Ch]
18010CD53  addss xmm1,[rcx+318h]
18010CD5B  mulss xmm1,[rel 1802697C0h]   ; Float32=0,5
18010CD63  xor r8d,r8d
18010CD66  movss [rcx+290h],xmm1
18010CD6E  movss [rcx+28Ch],xmm1
18010CD76  movss [rcx+294h],xmm1
18010CD7E  mov [rcx+298h],r8d
18010CD85  mov [rcx+2A4h],r8d
18010CD8C  cmp [rcx+33Ch],r8d
18010CD93  jle short 000000018010CDC4h
18010CD95  add rcx,8
18010CD99  nop dword [rax]
18010CDA0  mov eax,[rcx]
18010CDA2  mov dword [rcx+14h],0FFFFFFFFh
18010CDA9  lea rcx,[rcx+28h]
18010CDAD  dec eax
18010CDAF  cmp eax,1
18010CDB2  setbe al
18010CDB5  inc r8d
18010CDB8  mov [rcx-8],al
18010CDBB  cmp r8d,[rdx+33Ch]
18010CDC2  jl short 000000018010CDA0h
18010CDC4  ret
18010CDD0  push rbx
18010CDD2  sub rsp,50h
18010CDD6  xorps xmm0,xmm0
18010CDD9  movaps [rsp+20h],xmm8
18010CDDF  ucomisd xmm0,[rcx+2D0h]
18010CDE7  mov rbx,rcx
18010CDEA  jne short 000000018010CDF4h
18010CDEC  movsd [rcx+2D0h],xmm1
18010CDF4  movaps xmm0,xmm1
18010CDF7  subsd xmm0,[rcx+2D0h]
18010CDFF  movsd [rcx+2D0h],xmm1
18010CE07  mov rax,[rel 1802D3378h]
18010CE0E  cvtpd2ps xmm8,xmm0
18010CE13  cmp dword [rax+58h],2
18010CE17  jne near 000000018010CF9Ah
18010CE1D  mov [rsp+68h],rbp
18010CE22  xor ecx,ecx
18010CE24  movsxd rbp,[rbx+33Ch]
18010CE2B  mov [rsp+78h],rdi
18010CE30  test rbp,rbp
18010CE33  jle short 000000018010CE5Bh
18010CE35  lea rax,[rbx+1Ch]
18010CE39  mov r8,rbp
18010CE3C  nop dword [rax]
18010CE40  cmp dword [rax-14h],1
18010CE44  jbe short 000000018010CE51h
18010CE46  mov edx,[rax]
18010CE48  test edx,edx
18010CE4A  js short 000000018010CE51h
18010CE4C  cmp edx,ecx
18010CE4E  cmovg ecx,edx
18010CE51  add rax,28h
18010CE55  sub r8,1
18010CE59  jne short 000000018010CE40h
18010CE5B  mov ecx,[rel 1802CB7F8h]
18010CE61  mov edx,2
18010CE66  call qword [rel 180224650h]
18010CE6C  test al,al
18010CE6E  je short 000000018010CE98h
18010CE70  movss xmm3,[rbx+290h]
18010CE78  lea r8,[rel 180267FB8h]
18010CE7F  mov ecx,[rel 1802CB7F8h]
18010CE85  mov edx,2
18010CE8A  cvtps2pd xmm3,xmm3
18010CE8D  movq r9,xmm3
18010CE92  call qword [rel 180224560h]
18010CE98  xor edi,edi
18010CE9A  test rbp,rbp
18010CE9D  jle near 000000018010CF63h
18010CEA3  mov [rsp+70h],rsi
18010CEA8  nop dword [rax+rax]
18010CEB0  lea rsi,[rdi+rdi*4]
18010CEB4  cmp dword [rbx+rsi*8+8],1
18010CEB9  jbe short 000000018010CEF6h
18010CEBB  cmp dword [rbx+rsi*8+1Ch],0
18010CEC0  jl short 000000018010CEF6h
18010CEC2  mov ecx,[rel 1802CB7F8h]
18010CEC8  mov edx,2
18010CECD  call qword [rel 180224650h]
18010CED3  test al,al
18010CED5  je short 000000018010CF23h
18010CED7  mov r9d,[rbx+rsi*8+1Ch]
18010CEDC  lea r8,[rel 180267FB0h]   ; "%6d "
18010CEE3  mov ecx,[rel 1802CB7F8h]
18010CEE9  mov edx,2
18010CEEE  call qword [rel 180224560h]
18010CEF4  jmp short 000000018010CF23h
18010CEF6  mov ecx,[rel 1802CB7F8h]
18010CEFC  mov edx,2
18010CF01  call qword [rel 180224650h]
18010CF07  test al,al
18010CF09  je short 000000018010CF23h
18010CF0B  mov ecx,[rel 1802CB7F8h]
18010CF11  lea r8,[rel 180267FA8h]   ; "------ "
18010CF18  mov edx,2
18010CF1D  call qword [rel 180224560h]
18010CF23  cmp rdi,7
18010CF27  jne short 000000018010CF52h
18010CF29  mov ecx,[rel 1802CB7F8h]
18010CF2F  lea edx,[rdi-5]
18010CF32  call qword [rel 180224650h]
18010CF38  test al,al
18010CF3A  je short 000000018010CF52h
18010CF3C  mov ecx,[rel 1802CB7F8h]
18010CF42  lea r8,[rel 180257ADCh]
18010CF49  lea edx,[rdi-5]
18010CF4C  call qword [rel 180224560h]
18010CF52  inc rdi
18010CF55  cmp rdi,rbp
18010CF58  jl near 000000018010CEB0h
18010CF5E  mov rsi,[rsp+70h]
18010CF63  mov ecx,[rel 1802CB7F8h]
18010CF69  mov edx,2
18010CF6E  call qword [rel 180224650h]
18010CF74  mov rdi,[rsp+78h]
18010CF79  mov rbp,[rsp+68h]
18010CF7E  test al,al
18010CF80  je short 000000018010CF9Ah
18010CF82  mov ecx,[rel 1802CB7F8h]
18010CF88  lea r8,[rel 180257ADCh]
18010CF8F  mov edx,2
18010CF94  call qword [rel 180224560h]
18010CF9A  mov edx,[rbx+33Ch]
18010CFA0  xor ecx,ecx
18010CFA2  test edx,edx
18010CFA4  jle short 000000018010CFCDh
18010CFA6  lea rax,[rbx+8]
18010CFAA  nop word [rax+rax]
18010CFB0  cmp dword [rax],1
18010CFB3  jbe near 000000018010D34Ch
18010CFB9  cmp dword [rax+14h],0
18010CFBD  jl near 000000018010D34Ch
18010CFC3  inc ecx
18010CFC5  add rax,28h
18010CFC9  cmp ecx,edx
18010CFCB  jl short 000000018010CFB0h
18010CFCD  mov rcx,rbx
18010CFD0  movaps [rsp+40h],xmm6
18010CFD5  call 000000018010C870h
18010CFDA  movaps xmm6,xmm0
18010CFDD  movss [rsp+60h],xmm6
18010CFE3  mov eax,[rsp+60h]
18010CFE7  and eax,7F800000h
18010CFEC  cmp eax,7F800000h
18010CFF1  je near 000000018010D347h
18010CFF7  mov rax,[rel 1802D3378h]
18010CFFE  movaps [rsp+30h],xmm7
18010D003  cmp dword [rax+58h],2
18010D007  jne short 000000018010D042h
18010D009  mov ecx,[rel 1802CB7F8h]
18010D00F  mov edx,2
18010D014  call qword [rel 180224650h]
18010D01A  test al,al
18010D01C  je short 000000018010D042h
18010D01E  mov ecx,[rel 1802CB7F8h]
18010D024  lea r8,[rel 180267F98h]
18010D02B  xorps xmm3,xmm3
18010D02E  mov edx,2
18010D033  cvtss2sd xmm3,xmm6
18010D037  movq r9,xmm3
18010D03C  call qword [rel 180224560h]
18010D042  movsxd rax,[rbx+2A4h]
18010D049  cmp eax,0Ah
18010D04C  jae short 000000018010D05Fh
18010D04E  movss [rbx+rax*4+2A8h],xmm6
18010D057  inc dword [rbx+2A4h]
18010D05D  jmp short 000000018010D08Ah
18010D05F  xor edx,edx
18010D061  lea r8,[rbx+2A8h]
18010D068  nop dword [rax+rax]
18010D070  mov eax,[r8+4]
18010D074  inc edx
18010D076  mov [r8],eax
18010D079  lea r8,[r8+4]
18010D07D  cmp edx,9
18010D080  jb short 000000018010D070h
18010D082  movss [rbx+2CCh],xmm6
18010D08A  cmp dword [rbx+2A4h],0Ah
18010D091  xorps xmm7,xmm7
18010D094  jne short 000000018010D110h
18010D096  movss xmm3,[rel 18026979Ch]   ; Float32=0,2
18010D09E  lea r8,[rbx+2A8h]
18010D0A5  movaps xmm1,xmm7
18010D0A8  movaps xmm2,xmm7
18010D0AB  xor ecx,ecx
18010D0AD  nop dword [rax]
18010D0B0  lea eax,[rcx-5]
18010D0B3  inc ecx
18010D0B5  cdq
18010D0B6  xor eax,edx
18010D0B8  sub eax,edx
18010D0BA  movd xmm0,eax
18010D0BE  cvtdq2ps xmm0,xmm0
18010D0C1  mulss xmm0,xmm3
18010D0C5  addss xmm2,xmm0
18010D0C9  mulss xmm0,[r8]
18010D0CE  add r8,4
18010D0D2  addss xmm1,xmm0
18010D0D6  cmp ecx,0Ah
18010D0D9  jb short 000000018010D0B0h
18010D0DB  movss xmm0,[rbx+318h]
18010D0E3  divss xmm1,xmm2
18010D0E7  comiss xmm1,xmm0
18010D0EA  jb short 000000018010D0F8h
18010D0EC  movss xmm0,[rbx+31Ch]
18010D0F4  minss xmm0,xmm1
18010D0F8  movss [rsp+60h],xmm0
18010D0FE  mov eax,[rsp+60h]
18010D102  and eax,7F800000h
18010D107  cmp eax,7F800000h
18010D10C  je short 000000018010D147h
18010D10E  jmp short 000000018010D13Fh
18010D110  movss [rsp+60h],xmm6
18010D116  mov eax,[rsp+60h]
18010D11A  and eax,7F800000h
18010D11F  cmp eax,7F800000h
18010D124  je short 000000018010D147h
18010D126  movss xmm0,[rbx+318h]
18010D12E  comiss xmm6,xmm0
18010D131  jb short 000000018010D13Fh
18010D133  movss xmm0,[rbx+31Ch]
18010D13B  minss xmm0,xmm6
18010D13F  movss [rbx+28Ch],xmm0
18010D147  movss xmm0,[rbx+320h]
18010D14F  comiss xmm0,xmm7
18010D152  jbe short 000000018010D164h
18010D154  mulss xmm0,[rbx+28Ch]
18010D15C  movss [rbx+28Ch],xmm0
18010D164  movss xmm2,[rbx+330h]
18010D16C  maxss xmm8,xmm7
18010D171  ucomiss xmm2,xmm7
18010D174  movss xmm6,[rel 180269804h]   ; Float32=1
18010D17C  jne short 000000018010D18Fh
18010D17E  mov eax,[rbx+28Ch]
18010D184  mov [rbx+290h],eax
18010D18A  jmp 000000018010D224h
18010D18F  movss xmm4,[rbx+28Ch]
18010D197  movss xmm3,[rbx+290h]
18010D19F  comiss xmm4,xmm3
18010D1A2  jae short 000000018010D1D1h
18010D1A4  movaps xmm1,xmm2
18010D1A7  subss xmm3,xmm4
18010D1AB  mulss xmm1,[rbx+334h]
18010D1B3  movaps xmm0,xmm1
18010D1B6  subss xmm0,xmm2
18010D1BA  mulss xmm3,xmm0
18010D1BE  mulss xmm3,[rel 1802697D8h]   ; Float32=0,6666667
18010D1C6  addss xmm3,xmm2
18010D1CA  movaps xmm2,xmm3
18010D1CD  minss xmm2,xmm1
18010D1D1  mulss xmm2,xmm8
18010D1D6  comiss xmm2,xmm7
18010D1D9  jae short 000000018010D1E0h
18010D1DB  movaps xmm2,xmm7
18010D1DE  jmp short 000000018010D1E4h
18010D1E0  minss xmm2,xmm6
18010D1E4  movaps xmm1,xmm6
18010D1E7  movaps xmm0,xmm4
18010D1EA  subss xmm1,xmm2
18010D1EE  mulss xmm0,xmm2
18010D1F2  mulss xmm1,[rbx+290h]
18010D1FA  addss xmm1,xmm0
18010D1FE  movss [rsp+60h],xmm1
18010D204  mov eax,[rsp+60h]
18010D208  and eax,7F800000h
18010D20D  movss [rbx+290h],xmm1
18010D215  cmp eax,7F800000h
18010D21A  jne short 000000018010D224h
18010D21C  movss [rbx+290h],xmm4
18010D224  mov rax,[rel 1802D2EF8h]
18010D22B  mov ecx,[rel 1802BDA34h]
18010D231  mov edx,18h
18010D236  movss xmm8,[rax+54h]
18010D23C  mov rax,[gs:58h]
18010D245  mov rax,[rax+rcx*8]
18010D249  mov ecx,[rdx+rax]
18010D24C  cmp [rel 1802D4B48h],ecx
18010D252  jle short 000000018010D294h
18010D254  lea rcx,[rel 1802D4B48h]
18010D25B  call 00000001801F5890h
18010D260  cmp dword [rel 1802D4B48h],0FFFFFFFFh
18010D267  jne short 000000018010D294h
18010D269  lea rdx,[rel 180267F88h]   ; "mat_fullbright"
18010D270  lea rcx,[rel 1802D4B50h]
18010D277  call 00000001801E6380h
18010D27C  lea rcx,[rel 180222B70h]
18010D283  call 00000001801F569Ch
18010D288  lea rcx,[rel 1802D4B48h]
18010D28F  call 00000001801F5830h
18010D294  mov rax,[rel 1802D4B58h]
18010D29B  cmp dword [rax+58h],1
18010D29F  jne short 000000018010D2A7h
18010D2A1  movaps xmm8,xmm6
18010D2A5  jmp short 000000018010D2ADh
18010D2A7  comiss xmm8,xmm7
18010D2AB  jbe short 000000018010D2BAh
18010D2AD  mov rax,[rbx]
18010D2B0  movaps xmm1,xmm8
18010D2B4  mov rcx,rbx
18010D2B7  call qword [rax+38h]
18010D2BA  mov eax,[rbx+290h]
18010D2C0  comiss xmm7,[rbx+298h]
18010D2C7  mov [rbx+294h],eax
18010D2CD  movaps xmm7,[rsp+30h]
18010D2D2  jae short 000000018010D347h
18010D2D4  mov rcx,[rel 1802BB328h]
18010D2DB  mov rax,[rcx]
18010D2DE  call qword [rax+418h]
18010D2E4  subss xmm0,[rbx+2A0h]
18010D2EC  comiss xmm0,xmm6
18010D2EF  jbe short 000000018010D31Ch
18010D2F1  movss xmm0,[rbx+298h]
18010D2F9  mulss xmm0,[rel 1802697C0h]   ; Float32=0,5
18010D301  comiss xmm0,[rel 180269764h]   ; Float32=0,01
18010D308  movss [rbx+298h],xmm0
18010D310  jae short 000000018010D31Ch
18010D312  mov dword [rbx+298h],0
18010D31C  movss xmm0,[rbx+298h]
18010D324  subss xmm6,xmm0
18010D328  movaps xmm1,xmm0
18010D32B  mulss xmm1,[rbx+29Ch]
18010D333  mulss xmm6,[rbx+290h]
18010D33B  addss xmm1,xmm6
18010D33F  movss [rbx+294h],xmm1
18010D347  movaps xmm6,[rsp+40h]
18010D34C  movaps xmm8,[rsp+20h]
18010D352  add rsp,50h
18010D356  pop rbx
18010D357  ret
18010D360  mov r11,rsp
18010D363  mov [r11+8],rcx
18010D367  push rbp
18010D368  push rdi
18010D369  push r12
18010D36B  lea rbp,[r11-28h]
18010D36F  sub rsp,110h
18010D376  mov rax,[rel 1802D3378h]
18010D37D  mov r12,rdx
18010D380  mov rdi,rcx
18010D383  cmp dword [rax+58h],0
18010D387  je near 000000018010DB4Dh
18010D38D  movsxd rax,[rcx+33Ch]
18010D394  xor r10d,r10d
18010D397  movss xmm2,[rel 180269784h]   ; Float32=0,1
18010D39F  movss xmm3,[rel 180269978h]   ; Float32=-10
18010D3A7  mov [r11+10h],rbx
18010D3AB  xor ebx,ebx
18010D3AD  mov [r11-20h],rsi
18010D3B1  xor esi,esi
18010D3B3  mov [r11-28h],r13
18010D3B7  mov r13,rax
18010D3BA  mov [r11-30h],r14
18010D3BE  mov r14d,[r8+4]
18010D3C2  mov [r11-38h],r15
18010D3C6  mov r15d,[r8+8]
18010D3CA  movaps [r11-48h],xmm6
18010D3CF  movaps [r11-58h],xmm7
18010D3D4  movaps [r11-68h],xmm8
18010D3D9  movaps [r11-78h],xmm9
18010D3DE  movaps [r11-88h],xmm10
18010D3E6  movaps [r11-98h],xmm11
18010D3EE  movaps [r11-0A8h],xmm12
18010D3F6  mov r11d,[r8+0Ch]
18010D3FA  movd xmm0,eax
18010D3FE  movaps [rsp+70h],xmm13
