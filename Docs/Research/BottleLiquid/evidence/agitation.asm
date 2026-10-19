1809DBE00  mulss xmm1,xmm1
1809DBE04  addss xmm2,xmm0
1809DBE08  addss xmm2,xmm1
1809DBE0C  comiss xmm2,[rel 1810BA7A4h]   ; Float32=256
1809DBE13  jae short 00000001809DBE87h
1809DBE15  mov rax,[rbx]
1809DBE18  lea rdx,[rbp-20h]
1809DBE1C  mov rcx,rbx
1809DBE1F  call qword [rax+228h]
1809DBE25  movss xmm0,[rax+4]
1809DBE2A  movss xmm2,[rax]
1809DBE2E  movss xmm1,[rax+8]
1809DBE33  mulss xmm2,xmm2
1809DBE37  mulss xmm0,xmm0
1809DBE3B  mulss xmm1,xmm1
1809DBE3F  addss xmm2,xmm0
1809DBE43  addss xmm2,xmm1
1809DBE47  comiss xmm2,[rel 1810BA8ECh]   ; Float32=4096
1809DBE4E  jae short 00000001809DBE87h
1809DBE50  mov rax,[rdi]
1809DBE53  mov rcx,rdi
1809DBE56  call qword [rax+0D8h]
1809DBE5C  mov rsi,rax
1809DBE5F  cmp byte [rax+56h],19h
1809DBE63  je short 00000001809DBE87h
1809DBE65  mov r10,[rax]
1809DBE68  or r8d,0FFFFFFFFh
1809DBE6C  mov rcx,rax
1809DBE6F  movzx r9d,r12w
1809DBE73  lea edx,[r8+57h]
1809DBE77  call qword [r10+10h]
1809DBE7B  mov rcx,rsi
1809DBE7E  mov byte [rsi+56h],19h
1809DBE82  call 0000000180866B20h
1809DBE87  cmp [rdi+0C38h],r15b
1809DBE8E  je short 00000001809DBEA0h
1809DBE90  cmp [rdi+0CD0h],r15b
1809DBE97  jne short 00000001809DBEA0h
1809DBE99  mov [rdi+0C38h],r15b
1809DBEA0  cmp [rdi+0C3Ah],r15b
1809DBEA7  je short 00000001809DBEB9h
1809DBEA9  cmp [rdi+0CD0h],r15b
1809DBEB0  jne short 00000001809DBEB9h
1809DBEB2  mov [rdi+0C3Ah],r15b
1809DBEB9  mov rcx,rdi
1809DBEBC  call 000000018082D680h
1809DBEC1  test al,al
1809DBEC3  jne short 00000001809DBF03h
1809DBEC5  lea rcx,[rdi+0BF8h]
1809DBECC  movss [rsp+20h],xmm6
1809DBED2  lea r9,[rbp-20h]
1809DBED6  mov [rbp-18h],r15d
1809DBEDA  mov r8,rdi
1809DBEDD  mov [rbp-20h],r15
1809DBEE1  mov rdx,rdi
1809DBEE4  call 0000000180BD2BE0h
1809DBEE9  test byte [rbp-16h],1
1809DBEED  je short 00000001809DBF03h
1809DBEEF  mov rax,[rel 180CC1448h]
1809DBEF6  mov rdx,[rbp-20h]
1809DBEFA  mov rcx,[rax]
1809DBEFD  mov rax,[rcx]
1809DBF00  call qword [rax+28h]
1809DBF03  cmp [rdi+0CD0h],r15b
1809DBF0A  je short 00000001809DBF26h
1809DBF0C  test rbx,rbx
1809DBF0F  je short 00000001809DBF26h
1809DBF11  mov rax,[rbx]
1809DBF14  mov rcx,rbx
1809DBF17  call qword [rax+538h]
1809DBF1D  test al,al
1809DBF1F  je short 00000001809DBF26h
1809DBF21  mov sil,1
1809DBF24  jmp short 00000001809DBF5Eh
1809DBF26  xor sil,sil
1809DBF29  cmp [rdi+0C40h],sil
1809DBF30  je short 00000001809DBF5Eh
1809DBF32  test byte [rdi+24Eh],1
1809DBF39  je short 00000001809DBF5Eh
1809DBF3B  mov rcx,rdi
1809DBF3E  call 00000001809DBA60h
1809DBF43  test al,al
1809DBF45  je short 00000001809DBF5Eh
1809DBF47  lea rcx,[rel 181065C60h]   ; "Disabling motion on phys prop"
1809DBF4E  call qword [rel 180CC1450h]
1809DBF54  xor edx,edx
1809DBF56  mov rcx,rdi
1809DBF59  call 00000001801A5920h
1809DBF5E  cmp [rdi+0CD0h],r14b
1809DBF65  je short 00000001809DBF84h
1809DBF67  cmp [rdi+0C34h],r15d
1809DBF6E  jne short 00000001809DBF84h
1809DBF70  call 0000000180A8D0A0h
1809DBF75  mov rcx,rax
1809DBF78  lea rdx,[rdi+0A60h]
1809DBF7F  call 0000000180A90170h
1809DBF84  test sil,sil
1809DBF87  je short 00000001809DBF97h
1809DBF89  test r14b,r14b
1809DBF8C  jne short 00000001809DBFDAh
1809DBF8E  lea rcx,[rdi+0AB8h]
1809DBF95  jmp short 00000001809DBFA3h
1809DBF97  test r14b,r14b
1809DBF9A  je short 00000001809DBFDAh
1809DBF9C  lea rcx,[rdi+0AE0h]
1809DBFA3  lea r9,[rbp-20h]
1809DBFA7  movss [rsp+20h],xmm6
1809DBFAD  mov r8,rdi
1809DBFB0  mov [rbp-18h],r15d
1809DBFB4  mov rdx,rdi
1809DBFB7  mov [rbp-20h],r15
1809DBFBB  call 0000000180BD2BE0h
1809DBFC0  test byte [rbp-16h],1
1809DBFC4  je short 00000001809DBFDAh
1809DBFC6  mov rax,[rel 180CC1448h]
1809DBFCD  mov rdx,[rbp-20h]
1809DBFD1  mov rcx,[rax]
1809DBFD4  mov rax,[rcx]
1809DBFD7  call qword [rax+28h]
1809DBFDA  cmp [rdi+0C91h],r15b
1809DBFE1  je short 00000001809DBFEBh
1809DBFE3  mov rcx,rdi
1809DBFE6  call 00000001809DC010h
1809DBFEB  movaps xmm6,[rsp+40h]
1809DBFF0  lea r11,[rsp+50h]
1809DBFF5  mov rbx,[r11+20h]
1809DBFF9  mov rsi,[r11+28h]
1809DBFFD  mov rdi,[r11+30h]
1809DC001  mov r12,[r11+38h]
1809DC005  mov rsp,r11
1809DC008  pop r15
1809DC00A  pop r14
1809DC00C  pop rbp
1809DC00D  ret
1809DC00E  int3
1809DC00F  int3
1809DC010  push rbp
1809DC012  push rbx
1809DC013  lea rbp,[rsp-8]
1809DC018  sub rsp,108h
1809DC01F  mov rbx,rcx
1809DC022  mov rcx,[rcx+38h]
1809DC026  test rcx,rcx
1809DC029  je near 00000001809DC8DBh
1809DC02F  mov rcx,[rcx+8]
1809DC033  mov rax,[rcx]
1809DC036  call qword [rax+48h]
1809DC039  test rax,rax
1809DC03C  je near 00000001809DC8DBh
1809DC042  mov [rsp+130h],rdi
1809DC04A  mov rdi,[rax+250h]
1809DC051  test rdi,rdi
1809DC054  je near 00000001809DC8D3h
1809DC05A  mov rax,[rdi]
1809DC05D  mov rcx,rdi
1809DC060  call qword [rax+60h]
1809DC063  test eax,eax
1809DC065  jle near 00000001809DC8D3h
1809DC06B  mov rax,[rdi]
1809DC06E  xor edx,edx
1809DC070  mov rcx,rdi
1809DC073  call qword [rax+70h]
1809DC076  mov rdi,rax
1809DC079  test rax,rax
1809DC07C  je near 00000001809DC8D3h
1809DC082  test byte [rbx+24Ch],9
1809DC089  mov [rsp+138h],r14
1809DC091  mov [rsp+100h],r15
1809DC099  movzx r15d,byte [rbx+464h]
1809DC0A1  jne near 00000001809DC8E5h
1809DC0A7  cmp byte [rbx+0CD0h],0
1809DC0AE  jne short 00000001809DC0C6h
1809DC0B0  cmp byte [rbx+466h],0
1809DC0B7  ja short 00000001809DC0C6h
1809DC0B9  cmp byte [rbx+465h],0
1809DC0C0  jbe near 00000001809DC8C3h
1809DC0C6  mov rax,[rax]
1809DC0C9  lea rdx,[rsp+50h]
1809DC0CE  mov [rsp+128h],rsi
1809DC0D6  mov rcx,rdi
1809DC0D9  movaps [rsp+0F0h],xmm6
1809DC0E1  movaps [rsp+0E0h],xmm7
1809DC0E9  movaps [rsp+0D0h],xmm8
1809DC0F2  movaps [rsp+0C0h],xmm9
1809DC0FB  movaps [rsp+0B0h],xmm12
1809DC104  movaps [rsp+0A0h],xmm13
1809DC10D  movaps [rsp+90h],xmm14
1809DC116  call qword [rax+200h]
1809DC11C  mov rax,[rdi]
1809DC11F  lea rdx,[rsp+60h]
1809DC124  mov rcx,rdi
1809DC127  call qword [rax+228h]
1809DC12D  movss xmm1,[rsp+50h]
1809DC133  subss xmm1,[rbx+0C94h]
1809DC13B  movss xmm2,[rsp+54h]
1809DC141  subss xmm2,[rbx+0C98h]
1809DC149  movss xmm9,[rsp+58h]
1809DC150  movss xmm8,[rsp+60h]
1809DC157  movaps xmm0,xmm9
1809DC15B  subss xmm0,[rbx+0C9Ch]
1809DC163  movss xmm7,[rsp+64h]
1809DC169  movss xmm6,[rsp+68h]
1809DC16F  mov rax,[rel 1814A15C8h]
1809DC176  movss xmm13,[rel 1810B9FB0h]   ; Float32=1
1809DC17F  mulss xmm2,xmm2
1809DC183  mulss xmm1,xmm1
1809DC187  mulss xmm0,xmm0
1809DC18B  addss xmm2,xmm1
1809DC18F  movaps xmm1,xmm8
1809DC193  subss xmm1,[rbx+0CA0h]
1809DC19B  addss xmm2,xmm0
1809DC19F  movaps xmm0,xmm6
1809DC1A2  subss xmm0,[rbx+0CA8h]
1809DC1AA  mulss xmm1,xmm1
1809DC1AE  movss [rbp+20h],xmm2
1809DC1B3  movaps xmm2,xmm7
1809DC1B6  subss xmm2,[rbx+0CA4h]
1809DC1BE  sqrtss xmm4,[rbp+20h]
1809DC1C3  mulss xmm0,xmm0
1809DC1C7  mulss xmm2,xmm2
1809DC1CB  addss xmm2,xmm1
1809DC1CF  movaps xmm1,xmm13
1809DC1D3  subss xmm1,[rax+54h]
1809DC1D8  addss xmm2,xmm0
1809DC1DC  movss [rbp+20h],xmm2
1809DC1E1  movss xmm0,[rbp+20h]
1809DC1E6  movaps xmm3,xmm0
1809DC1E9  sqrtss xmm3,xmm0
1809DC1ED  addss xmm3,xmm4
1809DC1F1  movaps xmm0,xmm3
1809DC1F4  call 0000000180C89850h
1809DC1F9  movss xmm2,[rsp+50h]
1809DC1FF  movaps xmm12,xmm0
1809DC203  subss xmm2,[rbx+0C94h]
1809DC20B  movss xmm3,[rsp+54h]
1809DC211  movaps xmm1,xmm9
1809DC215  subss xmm1,[rbx+0C9Ch]
1809DC21D  subss xmm3,[rbx+0C98h]
1809DC225  movaps xmm0,xmm6
1809DC228  subss xmm0,[rbx+0CA8h]
1809DC230  mulss xmm2,xmm2
1809DC234  mulss xmm1,xmm1
1809DC238  mulss xmm3,xmm3
1809DC23C  mulss xmm0,xmm0
1809DC240  addss xmm3,xmm2
1809DC244  movaps xmm2,xmm7
1809DC247  subss xmm2,[rbx+0CA4h]
1809DC24F  addss xmm3,xmm1
1809DC253  movaps xmm1,xmm8
1809DC257  subss xmm1,[rbx+0CA0h]
1809DC25F  mulss xmm2,xmm2
1809DC263  movss [rbp+20h],xmm3
1809DC268  sqrtss xmm4,[rbp+20h]
1809DC26D  mulss xmm1,xmm1
1809DC271  addss xmm2,xmm1
1809DC275  addss xmm2,xmm0
1809DC279  movss [rbp+20h],xmm2
1809DC27E  movss xmm0,[rbp+20h]
1809DC283  movaps xmm1,xmm13
1809DC287  mov rax,[rel 1814A1898h]
1809DC28E  movaps xmm3,xmm0
1809DC291  sqrtss xmm3,xmm0
1809DC295  subss xmm1,[rax+54h]
1809DC29A  addss xmm3,xmm4
1809DC29E  movaps xmm0,xmm3
1809DC2A1  call 0000000180C89850h
1809DC2A6  addss xmm8,[rsp+50h]
1809DC2AD  addss xmm7,[rsp+54h]
1809DC2B3  addss xmm6,xmm9
1809DC2B8  mov rcx,rbx
1809DC2BB  lea rdx,[rsp+70h]
1809DC2C0  movaps xmm14,xmm0
1809DC2C4  movaps xmm1,xmm8
1809DC2C8  unpcklps xmm1,xmm7
1809DC2CB  movss [rbp-78h],xmm6
1809DC2D0  mov eax,[rbp-78h]
1809DC2D3  movsd [rsp+70h],xmm1
1809DC2D9  mov [rsp+78h],eax
1809DC2DD  call 00000001809D94B0h
1809DC2E2  movss xmm0,[rsp+50h]
1809DC2E8  xorps xmm6,xmm6
1809DC2EB  movss xmm1,[rsp+54h]
1809DC2F1  movss xmm9,[rel 1810B9D9Ch]   ; Float32=0,1
1809DC2FA  comiss xmm12,xmm9
1809DC2FE  movss [rbx+0C94h],xmm0
1809DC306  movss xmm0,[rsp+58h]
1809DC30C  movss [rbx+0C98h],xmm1
1809DC314  movss xmm1,[rsp+60h]
1809DC31A  movss [rbx+0C9Ch],xmm0
1809DC322  movss xmm0,[rsp+64h]
1809DC328  movss [rbx+0CA0h],xmm1
1809DC330  movss xmm1,[rsp+68h]
1809DC336  movss [rbx+0CA8h],xmm1
1809DC33E  movss [rbx+0CA4h],xmm0
1809DC346  jbe near 00000001809DC483h
1809DC34C  mov rax,[rel 1814A1388h]
1809DC353  mov rdi,[rel 18125D8F0h]
1809DC35A  movss xmm7,[rax+54h]
1809DC35F  addss xmm7,[rbx+0CC0h]
1809DC367  test rdi,rdi
1809DC36A  jne short 00000001809DC371h
1809DC36C  movaps xmm0,xmm6
1809DC36F  jmp short 00000001809DC392h
1809DC371  cmp byte [rdi+45h],0
1809DC375  jne short 00000001809DC38Dh
1809DC377  cmp byte [rdi+44h],0
1809DC37B  jne short 00000001809DC38Dh
1809DC37D  mov rax,[rdi+28h]
1809DC381  test rax,rax
1809DC384  je short 00000001809DC38Dh
1809DC386  mov ecx,1
1809DC38B  call rax
1809DC38D  movss xmm0,[rdi+34h]
1809DC392  comiss xmm7,xmm0
1809DC395  jbe near 00000001809DC430h
1809DC39B  mov rdi,[rel 18125D8F0h]
1809DC3A2  test rdi,rdi
1809DC3A5  jne short 00000001809DC3ACh
1809DC3A7  movaps xmm0,xmm6
1809DC3AA  jmp short 00000001809DC3CDh
1809DC3AC  cmp byte [rdi+45h],0
1809DC3B0  jne short 00000001809DC3C8h
1809DC3B2  cmp byte [rdi+44h],0
1809DC3B6  jne short 00000001809DC3C8h
1809DC3B8  mov rax,[rdi+28h]
1809DC3BC  test rax,rax
1809DC3BF  je short 00000001809DC3C8h
1809DC3C1  mov ecx,1
1809DC3C6  call rax
1809DC3C8  movss xmm0,[rdi+34h]
1809DC3CD  mov rax,[rel 1814A1388h]
1809DC3D4  subss xmm7,xmm0
1809DC3D8  movaps xmm0,xmm12
1809DC3DC  divss xmm7,[rax+54h]
1809DC3E1  mov rax,[rel 1814A14A8h]
1809DC3E8  movss xmm8,[rax+54h]
1809DC3EE  divss xmm0,xmm8
1809DC3F3  comiss xmm0,xmm6
1809DC3F6  jae short 00000001809DC3FDh
1809DC3F8  movaps xmm0,xmm6
1809DC3FB  jmp short 00000001809DC402h
1809DC3FD  minss xmm0,xmm13
1809DC402  mov rax,[rel 1814A1538h]
1809DC409  movss xmm1,[rax+54h]
1809DC40E  call 0000000180C89850h
1809DC413  mulss xmm7,[rbx+0CBCh]
1809DC41B  mulss xmm0,xmm12
1809DC420  addss xmm0,xmm7
1809DC424  maxss xmm0,xmm12
1809DC429  minss xmm0,xmm8
1809DC42E  jmp short 00000001809DC441h
1809DC430  mov rax,[rel 1814A14A8h]
1809DC437  movss xmm0,[rax+54h]
1809DC43C  minss xmm0,xmm12
1809DC441  mov rdi,[rel 18125D8F0h]
1809DC448  movss [rbx+0CBCh],xmm0
1809DC450  test rdi,rdi
1809DC453  jne short 00000001809DC45Ah
1809DC455  movaps xmm0,xmm6
1809DC458  jmp short 00000001809DC47Bh
1809DC45A  cmp byte [rdi+45h],0
1809DC45E  jne short 00000001809DC476h
1809DC460  cmp byte [rdi+44h],0
1809DC464  jne short 00000001809DC476h
1809DC466  mov rax,[rdi+28h]
1809DC46A  test rax,rax
1809DC46D  je short 00000001809DC476h
1809DC46F  mov ecx,1
1809DC474  call rax
1809DC476  movss xmm0,[rdi+34h]
1809DC47B  movss [rbx+0CC0h],xmm0
1809DC483  comiss xmm14,xmm9
1809DC487  jbe near 00000001809DC5C4h
1809DC48D  mov rax,[rel 1814A16E8h]
1809DC494  mov rdi,[rel 18125D8F0h]
1809DC49B  movss xmm7,[rax+54h]
1809DC4A0  addss xmm7,[rbx+0CCCh]
1809DC4A8  test rdi,rdi
1809DC4AB  jne short 00000001809DC4B2h
1809DC4AD  movaps xmm0,xmm6
1809DC4B0  jmp short 00000001809DC4D3h
1809DC4B2  cmp byte [rdi+45h],0
1809DC4B6  jne short 00000001809DC4CEh
1809DC4B8  cmp byte [rdi+44h],0
1809DC4BC  jne short 00000001809DC4CEh
1809DC4BE  mov rax,[rdi+28h]
1809DC4C2  test rax,rax
1809DC4C5  je short 00000001809DC4CEh
1809DC4C7  mov ecx,1
1809DC4CC  call rax
1809DC4CE  movss xmm0,[rdi+34h]
1809DC4D3  comiss xmm7,xmm0
1809DC4D6  jbe near 00000001809DC571h
1809DC4DC  mov rdi,[rel 18125D8F0h]
1809DC4E3  test rdi,rdi
1809DC4E6  jne short 00000001809DC4EDh
1809DC4E8  movaps xmm0,xmm6
1809DC4EB  jmp short 00000001809DC50Eh
1809DC4ED  cmp byte [rdi+45h],0
1809DC4F1  jne short 00000001809DC509h
1809DC4F3  cmp byte [rdi+44h],0
1809DC4F7  jne short 00000001809DC509h
1809DC4F9  mov rax,[rdi+28h]
1809DC4FD  test rax,rax
1809DC500  je short 00000001809DC509h
1809DC502  mov ecx,1
1809DC507  call rax
1809DC509  movss xmm0,[rdi+34h]
1809DC50E  mov rax,[rel 1814A16E8h]
1809DC515  subss xmm7,xmm0
1809DC519  movaps xmm0,xmm12
1809DC51D  divss xmm7,[rax+54h]
1809DC522  mov rax,[rel 1814A1778h]
1809DC529  movss xmm8,[rax+54h]
1809DC52F  divss xmm0,xmm8
1809DC534  comiss xmm0,xmm6
1809DC537  jae short 00000001809DC53Eh
1809DC539  movaps xmm0,xmm6
1809DC53C  jmp short 00000001809DC543h
1809DC53E  minss xmm0,xmm13
1809DC543  mov rax,[rel 1814A1808h]
1809DC54A  movss xmm1,[rax+54h]
1809DC54F  call 0000000180C89850h
1809DC554  mulss xmm7,[rbx+0CC8h]
1809DC55C  mulss xmm0,xmm12
1809DC561  addss xmm0,xmm7
1809DC565  maxss xmm0,xmm12
1809DC56A  minss xmm0,xmm8
1809DC56F  jmp short 00000001809DC582h
1809DC571  mov rax,[rel 1814A1778h]
1809DC578  movss xmm0,[rax+54h]
1809DC57D  minss xmm0,xmm12
1809DC582  mov rdi,[rel 18125D8F0h]
1809DC589  movss [rbx+0CC8h],xmm0
1809DC591  test rdi,rdi
1809DC594  jne short 00000001809DC59Bh
1809DC596  movaps xmm0,xmm6
1809DC599  jmp short 00000001809DC5BCh
1809DC59B  cmp byte [rdi+45h],0
1809DC59F  jne short 00000001809DC5B7h
1809DC5A1  cmp byte [rdi+44h],0
1809DC5A5  jne short 00000001809DC5B7h
1809DC5A7  mov rax,[rdi+28h]
1809DC5AB  test rax,rax
1809DC5AE  je short 00000001809DC5B7h
1809DC5B0  mov ecx,1
1809DC5B5  call rax
1809DC5B7  movss xmm0,[rdi+34h]
1809DC5BC  movss [rbx+0CCCh],xmm0
1809DC5C4  mov rax,[rel 1814A1388h]
1809DC5CB  mov rdi,[rel 18125D8F0h]
1809DC5D2  movaps xmm8,[rsp+0D0h]
1809DC5DB  movss xmm7,[rax+54h]
1809DC5E0  test rdi,rdi
1809DC5E3  jne short 00000001809DC5EAh
1809DC5E5  movaps xmm1,xmm6
1809DC5E8  jmp short 00000001809DC60Bh
1809DC5EA  cmp byte [rdi+45h],0
1809DC5EE  jne short 00000001809DC606h
1809DC5F0  cmp byte [rdi+44h],0
1809DC5F4  jne short 00000001809DC606h
1809DC5F6  mov rax,[rdi+28h]
1809DC5FA  test rax,rax
1809DC5FD  je short 00000001809DC606h
1809DC5FF  mov ecx,1
1809DC604  call rax
1809DC606  movss xmm1,[rdi+34h]
1809DC60B  ucomiss xmm7,xmm6
1809DC60E  movss xmm2,[rbx+0CBCh]
1809DC616  subss xmm1,[rbx+0CC0h]
1809DC61E  jne short 00000001809DC62Eh
1809DC620  subss xmm1,xmm7
1809DC624  comiss xmm1,xmm6
1809DC627  jb short 00000001809DC650h
1809DC629  movaps xmm2,xmm6
1809DC62C  jmp short 00000001809DC650h
1809DC62E  divss xmm1,xmm7
1809DC632  comiss xmm1,xmm6
1809DC635  jae short 00000001809DC63Ch
1809DC637  movaps xmm1,xmm6
1809DC63A  jmp short 00000001809DC641h
1809DC63C  minss xmm1,xmm13
1809DC641  movaps xmm0,xmm13
1809DC645  subss xmm0,xmm1
1809DC649  mulss xmm0,xmm2
1809DC64D  movaps xmm2,xmm0
1809DC650  mov rdi,[rel 18125D8F0h]
1809DC657  movss [rbx+0CB8h],xmm2
1809DC65F  mov rax,[rel 1814A16E8h]
1809DC666  movss xmm7,[rax+54h]
1809DC66B  test rdi,rdi
1809DC66E  jne short 00000001809DC675h
1809DC670  movaps xmm0,xmm6
1809DC673  jmp short 00000001809DC696h
1809DC675  cmp byte [rdi+45h],0
1809DC679  jne short 00000001809DC691h
1809DC67B  cmp byte [rdi+44h],0
1809DC67F  jne short 00000001809DC691h
1809DC681  mov rax,[rdi+28h]
1809DC685  test rax,rax
1809DC688  je short 00000001809DC691h
1809DC68A  mov ecx,1
1809DC68F  call rax
1809DC691  movss xmm0,[rdi+34h]
1809DC696  ucomiss xmm7,xmm6
1809DC699  movss xmm4,[rbx+0CC8h]
1809DC6A1  subss xmm0,[rbx+0CCCh]
1809DC6A9  jne short 00000001809DC6BEh
1809DC6AB  subss xmm0,xmm7
1809DC6AF  comiss xmm0,xmm6
1809DC6B2  jb short 00000001809DC6B9h
1809DC6B4  movaps xmm1,xmm6
1809DC6B7  jmp short 00000001809DC6DDh
1809DC6B9  movaps xmm1,xmm4
1809DC6BC  jmp short 00000001809DC6DDh
1809DC6BE  divss xmm0,xmm7
1809DC6C2  comiss xmm0,xmm6
1809DC6C5  jae short 00000001809DC6CCh
1809DC6C7  movaps xmm0,xmm6
1809DC6CA  jmp short 00000001809DC6D1h
1809DC6CC  minss xmm0,xmm13
1809DC6D1  movaps xmm1,xmm13
1809DC6D5  subss xmm1,xmm0
1809DC6D9  mulss xmm1,xmm4
1809DC6DD  movss xmm7,[rbx+0CB8h]
1809DC6E5  movss xmm2,[rel 1810BA7A0h]   ; Float32=255
1809DC6ED  movaps xmm0,xmm7
1809DC6F0  movss [rbx+0CC4h],xmm1
1809DC6F8  mov rax,[rel 1814A14A8h]
1809DC6FF  movss xmm3,[rax+54h]
1809DC704  ucomiss xmm3,xmm6
1809DC707  jne short 00000001809DC71Ch
1809DC709  subss xmm0,xmm3
1809DC70D  comiss xmm0,xmm6
1809DC710  jb short 00000001809DC717h
1809DC712  movaps xmm0,xmm2
1809DC715  jmp short 00000001809DC733h
1809DC717  movaps xmm0,xmm6
1809DC71A  jmp short 00000001809DC733h
1809DC71C  divss xmm0,xmm3
1809DC720  comiss xmm0,xmm6
1809DC723  jae short 00000001809DC72Ah
1809DC725  movaps xmm0,xmm6
1809DC728  jmp short 00000001809DC72Fh
1809DC72A  minss xmm0,xmm13
1809DC72F  mulss xmm0,xmm2
1809DC733  mov rax,[rel 1814A1778h]
1809DC73A  cvttss2si rsi,xmm0
1809DC73F  movss xmm3,[rax+54h]
1809DC744  movaps xmm0,xmm1
1809DC747  ucomiss xmm3,xmm6
1809DC74A  jne short 00000001809DC75Ah
1809DC74C  subss xmm0,xmm3
1809DC750  comiss xmm0,xmm6
1809DC753  jae short 00000001809DC774h
1809DC755  movaps xmm2,xmm6
1809DC758  jmp short 00000001809DC774h
1809DC75A  divss xmm0,xmm3
1809DC75E  comiss xmm0,xmm6
1809DC761  jae short 00000001809DC768h
1809DC763  movaps xmm0,xmm6
1809DC766  jmp short 00000001809DC76Dh
1809DC768  minss xmm0,xmm13
1809DC76D  mulss xmm0,xmm2
1809DC771  movaps xmm2,xmm0
1809DC774  mov rax,[rel 1814A1658h]
1809DC77B  movaps xmm13,[rsp+0A0h]
1809DC784  cvttss2si rdi,xmm2
1809DC789  cmp dword [rax+58h],0
1809DC78D  je short 00000001809DC7FCh
1809DC78F  test sil,sil
1809DC792  jne short 00000001809DC799h
1809DC794  test dil,dil
1809DC797  je short 00000001809DC7FCh
1809DC799  movss xmm2,[rbx+0CBCh]
1809DC7A1  lea rcx,[rel 181065CA0h]
1809DC7A8  cvtps2pd xmm0,xmm1
1809DC7AB  movzx eax,dil
1809DC7AF  mov [rsp+40h],eax
1809DC7B3  movzx r8d,sil
1809DC7B7  xorps xmm5,xmm5
1809DC7BA  xorps xmm1,xmm1
1809DC7BD  movsd [rsp+38h],xmm0
1809DC7C3  cvtps2pd xmm4,xmm4
1809DC7C6  cvtss2sd xmm5,xmm14
1809DC7CB  movsd [rsp+30h],xmm4
1809DC7D1  cvtps2pd xmm3,xmm7
1809DC7D4  cvtps2pd xmm2,xmm2
1809DC7D7  cvtss2sd xmm1,xmm12
1809DC7DC  movsd [rsp+28h],xmm5
1809DC7E2  movq r9,xmm3
1809DC7E7  mov [rsp+20h],r8d
1809DC7EC  movq rdx,xmm1
1809DC7F1  movq r8,xmm2
1809DC7F6  call qword [rel 180CC1478h]
1809DC7FC  movzx eax,byte [rbx+467h]
