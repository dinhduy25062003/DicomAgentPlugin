# Cấu hình Prod và kết nối máy X-quang

Tài liệu hướng dẫn vận hành bản Agent hiện có. Cài phần mềm theo [Windows](HUONG-DAN-WINDOWS.md) hoặc [Linux](HUONG-DAN-LINUX.md), kiểm thử bằng SCU trước, sau đó cấu hình máy X-quang gửi DICOM đến Agent.

Agent là Storage SCP: nhận ảnh do máy chụp/console gửi bằng C-STORE và lưu `.dcm`. Agent không điều khiển máy chụp, không kích hoạt phát tia và không lấy ảnh bằng Query/Retrieve. Thao tác chụp thực tế do nhân viên được đào tạo thực hiện theo quy trình của thiết bị.

## 1. Chốt thông tin triển khai

Điền thông tin thực tế trước khi cấu hình. Các giá trị dưới đây chỉ là ví dụ, cần thay cho phù hợp mạng cơ sở.

| Thông tin | Ví dụ |
|---|---|
| Máy cài Agent | Máy Windows/Linux lưu ảnh của phòng khám |
| IP LAN Agent, cố định hoặc DHCP reservation | `192.168.1.10` |
| AE Title Agent | `DICOM_AGENT` |
| Port DICOM TCP | `11112` |
| IP thiết bị/console thực sự gửi ảnh | `192.168.1.20` |
| Calling AE thiết bị/console | `XRAY_ROOM_1` |
| Storage | Mặc định OS hoặc ổ dữ liệu đã cấp quyền |
| Nhân sự | Quản trị máy/mạng, kỹ thuật viên hãng máy, người vận hành |

Máy gửi thường là acquisition workstation/console, không nhất thiết là IP đầu phát tia hay detector. Xác nhận IP và AE từ cấu hình DICOM của console hoặc kỹ thuật viên hãng. Ghi thêm hãng/model/phiên bản console và DICOM Conformance Statement để đối chiếu tương thích.

## 2. Kiểm tra khả năng tương thích

| Chức năng | Bản hiện tại |
|---|---|
| C-ECHO | Có, Agent nhận kiểm tra kết nối |
| C-STORE | Có, Agent nhận và lưu ảnh |
| CR, DX presentation/processing | Có trong danh sách Storage SOP hỗ trợ |
| Transfer Syntax | Explicit VR Little Endian `1.2.840.10008.1.2.1`, Implicit VR Little Endian `1.2.840.10008.1.2` |
| JPEG/JPEG-LS/JPEG 2000/RLE/Big Endian | Không hỗ trợ nhận theo các syntax này; không tự transcode |
| C-FIND, C-MOVE, Modality Worklist | Không hỗ trợ |
| Storage Commitment, DICOMweb, DICOM TLS | Không hỗ trợ |
| Viewer, xuất JPEG/PNG, upload website tự động | Chưa có |

Yêu cầu hãng cấu hình gửi ảnh **uncompressed Little Endian** và xác nhận SOP Class của máy nằm trong danh sách `StorageScp.cs`. Không mặc định máy chụp có thể gửi đúng định dạng chỉ vì C-ECHO thành công. Nếu thiết bị bắt buộc TLS, compressed transfer syntax hoặc Storage Commitment, cần giải quyết tương thích trước khi nghiệm thu.

## 3. Chuẩn bị mạng và máy Agent

1. Đặt IP Agent ổn định; bảo đảm console truy cập được IP/port qua mạng LAN/VLAN theo thiết kế mạng của cơ sở.
2. Nếu Agent nằm trong máy ảo, cấu hình đường truy cập từ console đến IP VM; kiểm tra IP nguồn sau NAT nếu có. `127.0.0.1` chỉ dùng khi gửi từ cùng máy Agent.
3. Cài service và cấp quyền operator theo hướng dẫn OS. Xác nhận service chạy sau khởi động lại máy.
4. Chuẩn bị ổ lưu trữ, dung lượng và kế hoạch sao lưu. Agent chưa tự xóa ảnh theo thời gian.
5. Chỉ đặt DICOM TCP trong LAN/VLAN/VPN được quản lý; không mở port nhận ảnh ra Internet. AE/IP allowlist không thay thế mã hóa hoặc xác thực mật mã.

## 4. Cấu hình Agent

Mở Desktop → **Refresh → Settings**, dùng ví dụ ở bước 1:

| Trường | Giá trị ví dụ |
|---|---|
| AE Title | `DICOM_AGENT` |
| Listen IP address | `192.168.1.10` hoặc `0.0.0.0` để nghe mọi IPv4 interface |
| Port | `11112` |
| Allowed Calling AE Titles | `XRAY_ROOM_1` |
| Allowed source IPs | `192.168.1.20` |
| Storage directory | Để trống hoặc đường dẫn tuyệt đối đã cấp quyền |
| Maximum concurrent associations | Ban đầu `10`, đánh giá theo thiết bị/tải thực tế |
| Maximum instance size (bytes) | Ban đầu `268435456` = 256 MiB mỗi instance; đối chiếu kích thước ảnh thực tế |
| Enable receiver / Enable file history | Bật cả hai |

Nếu có nhiều thiết bị, nhập danh sách AE và IP phân cách bằng dấu phẩy. Hai allowlist là hai tập độc lập, không ghép từng AE với từng IP. Danh sách rỗng cho phép mọi AE/IP có thể đến port, vì vậy điền rõ cho Prod.

AE Title dài 1–16 ký tự ASCII printable, không backslash, không khoảng trắng đầu/cuối; Calling AE phân biệt hoa/thường. Listen IP phải là IP của chính máy Agent hoặc địa chỉ wildcard, không nhập IP máy chụp vào đây. Allowlist IP dùng IP literal, không dùng hostname/CIDR.

Bấm **Validate → Save settings**, rồi restart:

**Windows, PowerShell Administrator:**

```powershell
Restart-Service DicomAgent
Get-Service DicomAgent
Get-NetTCPConnection -State Listen -LocalPort 11112
```

**Linux:**

```bash
sudo systemctl restart dicom-agent.service
systemctl status dicom-agent.service --no-pager
ss -ltn 'sport = :11112'
```

Desktop → **Refresh**, đối chiếu Dashboard với AE/IP/port/storage đang chạy. Mỗi lần đổi cấu hình đều cần restart; sắp xếp lúc thiết bị không đang gửi.

Thứ tự ưu tiên cấu hình: `appsettings.json` cạnh binary → `settings.json` trong data → biến môi trường prefix `DICOM_AGENT_` → CLI. Không sửa file cạnh binary để thay cho Settings; nếu giá trị không áp dụng sau restart, kiểm tra lớp môi trường/CLI đang ghi đè.

## 5. Storage và quyền ghi

Ưu tiên mặc định khi chưa cần ổ riêng:

- Windows: `C:\ProgramData\DicomAgent\Storage`.
- Linux: `/var/lib/dicom-agent/storage`.

File được lưu theo `YYYY-MM-DD/StudyInstanceUID/SeriesInstanceUID/SOPInstanceUID.dcm`, ngày UTC. Không đưa tên bệnh nhân vào đường dẫn. Tuy nhiên file DICOM và SQLite có thể chứa thông tin bệnh nhân, cần bảo vệ quyền truy cập và bản sao lưu.

### Windows: ví dụ ổ riêng `D:\DicomStorage`

Chỉ chạy khi ổ D: thực sự tồn tại; PowerShell Administrator:

```powershell
$storage = 'D:\DicomStorage'
New-Item -ItemType Directory -Force -Path $storage
icacls $storage /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' 'NT SERVICE\DicomAgent:(OI)(CI)M' 'DicomAgentOperators:(OI)(CI)RX'
```

Dùng thư mục mới dành riêng cho Agent. Với thư mục có sẵn, quản trị phải kiểm tra ACL hiện hữu; lệnh trên không bảo đảm loại bỏ mọi explicit grant cũ. Sau đó lưu Storage directory trong UI và restart. Không cấp Everyone Full Control.

### Linux: ví dụ `/srv/dicom-storage`

```bash
sudo install -d -o dicom-agent -g dicom-agent-operators -m 2750 /srv/dicom-storage
sudo systemctl edit dicom-agent.service
```

Thêm vào phần nội dung override:

```ini
[Service]
ReadWritePaths=/srv/dicom-storage
```

Lưu và chạy:

```bash
sudo systemctl daemon-reload
sudo systemctl restart dicom-agent.service
```

Sau khi sandbox cho phép thư mục mới, lưu Storage directory `/srv/dicom-storage` trong UI rồi restart service một lần nữa. Tránh storage trong home vì `ProtectHome=true`; không dùng symlink. Nếu là ổ mount riêng, bảo đảm mount có trước service, kiểm tra quyền và cấu hình phụ thuộc mount với quản trị hệ thống.

Đổi storage không tự di chuyển ảnh cũ; history cũ vẫn giữ, reconciliation chỉ quét root hiện hành. Một storage root chỉ thuộc một Host.

## 6. Firewall: chỉ cho phép máy gửi

Setup.exe và `.deb` không tự mở firewall LAN. Sau khi đã chốt đúng IP/port:

**Windows, PowerShell Administrator:**

```powershell
Get-NetConnectionProfile
New-NetFirewallRule -DisplayName 'DICOM Agent XRAY ROOM 1' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 11112 -RemoteAddress 192.168.1.20 -Profile Domain,Private -Program "$env:ProgramFiles\DicomAgent\host\DicomAgent.Host.exe"
```

Rule này tạo một lần; các lần thay đổi sau chỉnh rule hiện có, tránh tạo trùng. Nếu card mạng đang Public, xác nhận phân loại mạng với quản trị; không mở rộng sang Public để bỏ qua vấn đề. Kiểm tra các rule Allow rộng sẵn có vì rule giới hạn mới không tự vô hiệu hóa chúng.

**Linux dùng UFW:**

```bash
sudo ufw status verbose
sudo ufw allow from 192.168.1.20 to any port 11112 proto tcp
```

Nếu dùng firewall khác, quản trị tạo rule tương đương. Không bật UFW trực tiếp trên máy truy cập từ xa khi chưa bảo toàn đường quản trị. Kiểm tra cả firewall/VLAN ACL giữa hai máy.

Muốn dùng SCU từ một máy kiểm thử khác, thêm tạm IP và Calling AE của máy đó vào allowlist/firewall; sau khi thử xong, gỡ các quyền thử nghiệm và restart Agent để trở lại cấu hình Prod.

## 7. Cấu hình trên console máy X-quang

Tên menu thay đổi theo hãng/model, thường là DICOM Network, Remote Nodes, Destinations hoặc Storage. Cần kỹ thuật viên hãng nếu menu bị khóa; không có thao tác menu chung được xác nhận cho mọi thiết bị.

Tại console, thêm một **Storage destination / Remote DICOM node**:

| Trường trên console | Giá trị ví dụ | Ý nghĩa |
|---|---|---|
| Destination name | `DICOM Agent` | Nhãn hiển thị tùy chọn |
| Remote host / IP | `192.168.1.10` | IP máy Agent |
| Remote port | `11112` | Port Agent đang nghe |
| Remote AE / Called AE | `DICOM_AGENT` | AE của Agent |
| Local AE / Calling AE | `XRAY_ROOM_1` | AE gửi của console, phải khớp allowlist |
| Service / Role | Storage SCU ở phía console | Console gửi, Agent nhận Storage SCP |
| Transfer Syntax / Compression | Uncompressed Explicit hoặc Implicit VR Little Endian | Không JPEG/JPEG 2000/RLE |

Không thay IP/AE toàn cục đang dùng với PACS khác khi chưa đánh giá ảnh hưởng. Nếu Local AE chỉ là trường chỉ đọc, dùng đúng AE hiện hữu đó trong allowlist Agent. Tạo destination Agent riêng và chọn rõ đích gửi.

Lưu cấu hình theo hướng dẫn hãng; chỉ restart console nếu hãng yêu cầu. Chọn **Verify / Test / Echo** đối với node Agent. Kết quả cần là C-ECHO Success.

## 8. Gửi ảnh và nghiệm thu

1. Dùng ảnh kiểm thử được phép sử dụng trên console, hoặc quy trình phantom/test mode do hãng và cơ sở quy định. Không cần chụp một người chỉ để thử phần mềm nhận ảnh.
2. Chọn study/series/ảnh thử → **Send / Export DICOM / Archive**, chọn destination Agent vừa tạo.
3. Kiểm tra hàng đợi gửi trên console báo thành công. C-ECHO thành công chưa chứng minh C-STORE thành công.
4. Trên Agent, **Refresh → Received files**, đối chiếu Study/Series/SOP UID, modality, kích thước và thời gian. UI hiển thị tối đa 100 file gần nhất, không dùng số dòng UI làm tổng toàn bộ kho ảnh.
5. **Open file location**, kiểm tra `.dcm` hoàn chỉnh; mở bằng viewer DICOM độc lập, kiểm tra metadata và nội dung ảnh đúng với ảnh nguồn.
6. Nếu workflow có website, chọn file này upload thủ công và kiểm tra website đọc được. Việc nhận ảnh không phụ thuộc Internet/website.

Ghi kết quả thực tế cho từng mục:

| Kiểm tra | Kết quả: PASS / FAIL / NOT RUN và bằng chứng |
|---|---|
| C-ECHO từ console thật | |
| C-STORE ảnh CR/DX theo máy thực tế | |
| Số ảnh/UID nhận khớp số ảnh được gửi | |
| Xem lại metadata/pixel bằng viewer độc lập | |
| Ảnh lớn thực tế trong giới hạn | |
| Gửi lại cùng ảnh: không thêm bản sao nếu UID/nội dung giữ nguyên | |
| Ngắt/kết nối mạng lại: hàng đợi console retry theo cơ chế của hãng | |
| Đóng UI: Host tiếp tục nhận | |
| Reboot máy Agent: service chạy lại và nhận ảnh | |
| Khôi phục thử từ bản sao lưu trong môi trường kiểm thử | |

Một số console xuất lại ảnh với UID mới; khi đó Agent coi là instance mới, không phải duplicate cùng UID. Không dùng kết quả SCU loopback để thay cho nghiệm thu console thật. Bản hiện tại chưa có Storage Commitment; thống nhất với hãng/cơ sở tiêu chí lưu giữ và việc xóa ảnh nguồn, không suy diễn Success thành cam kết lưu trữ dài hạn.

## 9. Vận hành, sao lưu và bảo trì

- Theo dõi dung lượng storage, trạng thái service, lần nhận cuối và hàng đợi gửi console. Dashboard Recent errors chỉ giữ trong RAM của phiên Host.
- Bảo vệ/mã hóa ổ lưu trữ và backup theo chính sách cơ sở. Chỉ cấp operator cho người được phép xem dữ liệu và cấu hình.
- Sao lưu cả storage, `settings.json`, `history.db` và quyền truy cập. Để có bản sao nhất quán đơn giản: tạm ngừng gửi, dừng service, sao lưu, khởi động service, xác nhận console gửi tiếp. Nếu cần backup online phải dùng cơ chế SQLite phù hợp, không chỉ copy DB đang ghi rồi bỏ WAL/SHM.
- Thử phục hồi trên môi trường riêng. Không cho hai Host hoạt động trên cùng storage root.
- Khi DB lỗi sau khi ảnh đã commit, Agent vẫn có thể trả C-STORE Success vì file được giữ. Kiểm tra lỗi lịch sử; reconciliation thử mỗi 30 giây sau khi DB phục hồi. File có sẵn không có nghĩa history đã cập nhật đầy đủ.
- Khi nâng cấp, sao lưu trước, đóng Desktop/dừng gửi trong cửa sổ bảo trì; kiểm tra lại service, quyền và gửi ảnh sau nâng cấp. Gỡ ứng dụng giữ dữ liệu; không xem uninstall là thao tác xóa dữ liệu bệnh nhân.
- Xóa quyền loopback/SCU thử nghiệm khỏi allowlist và firewall sau nghiệm thu nếu không còn cần. Không để `XRAY_SIM` thay cho AE console thật.

## 10. Khoanh vùng lỗi

| Lỗi | Hướng kiểm tra |
|---|---|
| Console không đến được port | IP Agent ổn định, route/VLAN, firewall, VM, service listening |
| Association rejected | Called AE, Calling AE phân biệt hoa/thường, IP nguồn thực tế, cấu hình active sau restart |
| Echo được nhưng gửi ảnh lỗi | Storage SOP/transfer syntax, ảnh nén, giới hạn bytes, quyền ghi/dung lượng |
| C-STORE thành công nhưng không thấy UI | Refresh; storage active, UID, history/DB và giới hạn 100 dòng |
| File `.partial` sau sự cố | Không tự đổi đuôi hàng loạt; kiểm tra sự cố, gửi lại từ nguồn và đối chiếu |
| Cùng UID khác nội dung bị từ chối | Điều tra nguồn; Agent giữ file cũ, không xóa/overwrite để ép thành công |
| Storage ngoài mặc định bị chặn | Windows ACL; Linux owner/group, mount và `ReadWritePaths` |

Linux xem `journalctl -u dicom-agent.service -n 100 --no-pager`. Windows xem Dashboard Recent errors và nhật ký ứng dụng/service nếu được hệ thống ghi nhận. Khi cần hỗ trợ, cung cấp thời gian UTC, phiên bản, AE/IP/port, mã phản hồi, model console và thông báo lỗi đã loại dữ liệu bệnh nhân.

Nguồn đối chiếu nội bộ: [README](../README.md), [đóng gói](PACKAGES.md), [triển khai](DEPLOYMENT.md), [phạm vi kiểm chứng](VERIFICATION.md). Tài liệu này mô tả cấu hình theo mã nguồn hiện tại, không xác nhận đã nghiệm thu tại cơ sở hay trên model máy cụ thể.
